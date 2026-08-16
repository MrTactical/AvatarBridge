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
    // Small component conversions plus the final VRC-component cleanup:
    //   VRCHeadChop           -> FPRExclusion (CVR first-person hiding/showing)
    //   VRCSpatialAudioSource -> plain AudioSource spatial settings
    //   leftover VRC.* components + PipelineManager -> deleted
    public static class MiscConverter
    {
        public static void EnableAnimatedParticleEmitters(BridgeContext ctx)
        {
            var controller = ctx.MergedController;
            if (controller == null || ctx.Target == null)
            {
                return;
            }

            // Which paths each clip drives m_IsActive on.
            // The gate that makes enabling safe.
            var enabled = new List<string>();
            var otherModules = new SortedSet<string>(StableSampleOrder.Instance);
            var seen = new HashSet<ParticleSystem>();
            int stripped = 0;

            foreach (var clip in controller.animationClips.Distinct())
            {
                if (clip == null)
                {
                    continue;
                }
                var bindings = AnimationUtility.GetCurveBindings(clip);
                var togglesActive = new HashSet<string>(bindings
                    .Where(b => b.type == typeof(GameObject) && b.propertyName == "m_IsActive")
                    .Select(b => b.path));

                foreach (var binding in bindings)
                {
                    if (binding.type != typeof(ParticleSystem)
                        || !binding.propertyName.EndsWith("Module.enabled", System.StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var child = BridgeContext.FindByAnimationPath(ctx.Target.transform, binding.path);
                    var system = child != null ? child.GetComponent<ParticleSystem>() : null;
                    if (system == null)
                    {
                        continue;
                    }
                    if (binding.propertyName != "EmissionModule.enabled")
                    {
                        otherModules.Add($"{binding.path} ({binding.propertyName})");
                        continue;
                    }
                    var emission = system.emission;
                    if (!emission.enabled && togglesActive.Contains(binding.path) && seen.Add(system))
                    {
                        emission.enabled = true;
                        EditorUtility.SetDirty(system);
                        enabled.Add(binding.path);
                    }

                    // And take the curve out. The object's own m_IsActive
                    // gates the effect; a module curve beside it can
                    // only fight that.
                    // Only on a clip this conversion owns. Clips are
                    // shared, and editing one in place reaches every
                    // user of the real asset.
                    if (togglesActive.Contains(binding.path) && OwnedByThisConversion(ctx, clip))
                    {
                        AnimationUtility.SetEditorCurve(clip, binding, null);
                        stripped++;
                    }
                }
            }

            if (enabled.Count > 0)
            {
                ctx.Report.Converted("Particles",
                    $"{enabled.Count} particle emitter(s) switched on so remote players can see them",
                    $"{string.Join(", ", enabled.Take(6))}{(enabled.Count > 6 ? ", …" : "")} — these " +
                    "effects were authored with the emitter OFF and turned on by animating the particle " +
                    "system's emission module. Switching a GameObject on is something every client does; " +
                    "animating a particle MODULE is not the same kind of write, and where it doesn't land " +
                    "the object turns on and emits nothing — the effect is invisible to everyone but you. " +
                    "The emitter is now on permanently and the object's own on/off animation gates the " +
                    "effect instead, which is how the effects that already worked were built. Nothing " +
                    "plays at rest, because the same clip switches the object off." +
                    (stripped > 0
                        ? $" The {stripped} module curve(s) that switched it off again are removed " +
                          "from the converted clips — left in, they undo this every time the off " +
                          "state plays, which is at rest, always."
                        : ""));
            }
            ReportDefaultParticleMaterials(ctx);
            if (otherModules.Count > 0)
            {
                ctx.Report.Skipped("Particles",
                    $"{otherModules.Count} animated particle module(s) left as they are",
                    $"{string.Join(", ", otherModules.Take(6))}{(otherModules.Count > 6 ? ", …" : "")} — " +
                    "these animate a particle system module other than emission. If the effect looks " +
                    "wrong to other players but right to you, this is the first thing to suspect: " +
                    "rebuild it so the object's own on/off state drives the effect instead.");
            }
        }

        static bool OwnedByThisConversion(BridgeContext ctx, AnimationClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }
            if (string.IsNullOrEmpty(ctx.OutputDir))
            {
                return false;
            }
            return path.Replace('\\', '/')
                .StartsWith(ctx.OutputDir.Replace('\\', '/').TrimEnd('/') + "/",
                    System.StringComparison.OrdinalIgnoreCase);
        }

        static void ReportDefaultParticleMaterials(BridgeContext ctx)
        {
            if (ctx.Target == null)
            {
                return;
            }
            var plain = new SortedSet<string>(StableSampleOrder.Instance);
            foreach (var renderer in ctx.Target.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var material = renderer.sharedMaterial;
                if (material != null && material.name != "Default-ParticleSystem")
                {
                    continue;
                }
                plain.Add($"{ctx.PathInTarget(renderer.transform)}" +
                          (material == null ? " (no material at all)" : ""));
            }
            if (plain.Count == 0)
            {
                return;
            }
            ctx.Report.Warning("Particles",
                $"{plain.Count} particle system(s) are using Unity's default material",
                $"{string.Join(", ", plain.Take(6))}{(plain.Count > 6 ? ", …" : "")} — they draw as " +
                "plain untinted squares rather than whatever the effect is supposed to look like. " +
                "This is how the avatar was already built: conversion copies the material across " +
                "unchanged, and one on Unity's default was on Unity's default in VRChat too. It is " +
                "worth checking because the editor gives no hint — the effect only looks wrong once " +
                "somebody sees it in game. If a system is only there to spawn another one and is " +
                "never meant to be visible, turn its Renderer off rather than leaving it drawing.");
        }

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
            AvatarHygiene.NormalizeSkinnedBounds(ctx);
            AvatarHygiene.SanitizeAudioSources(ctx);
            GroundAnimationPoseRatio(ctx);
        }

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

        internal static void ConvertHeadChopsForTest(BridgeContext ctx) => ConvertHeadChops(ctx);

        static void ConvertHeadChops(BridgeContext ctx)
        {
            const string category = "Head chop";
            // Path of each VRC Head Chop GameObject -> the FPRExclusion transforms made from it,
            // so animations that toggled the head chop can be re-pointed at the exclusions.
            var pathToExclusions = new Dictionary<string, List<(Transform t, bool shownWhenActive)>>();

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
                            pathToExclusions[headChopPath] = list = new List<(Transform, bool)>();
                        }
                        list.Add((go.transform, isShown));
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

        static void RewriteHeadChopAnimations(BridgeContext ctx, Dictionary<string, List<(Transform t, bool shownWhenActive)>> map)
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
            // Every clone leaves its original behind in the controller,
            // unreferenced but still carrying dead head-chop curves
            // that read as live bugs to anyone grepping the file.
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
                // Only ever this controller's own sub-assets: never touch a clip that lives in the
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
            Dictionary<string, List<(Transform t, bool shownWhenActive)>> map, Dictionary<AnimationClip, AnimationClip> cache,
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
            Dictionary<string, List<(Transform t, bool shownWhenActive)>> map, Dictionary<AnimationClip, AnimationClip> cache,
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
            Dictionary<string, List<(Transform t, bool shownWhenActive)>> map, Dictionary<AnimationClip, AnimationClip> cache,
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
                .Where(b => b.type == typeof(VRCHeadChop))
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
                if (!map.TryGetValue(b.path, out var exclusions))
                {
                    // The chop this drove produced no exclusion.
                    // Dead either way; say so.
                    ctx.Report.Warning("Head chop",
                        $"\"{clip.name}\" animated a head chop that was not converted",
                        $"{b.path} ({b.propertyName}) — the head chop there was skipped, so this " +
                        "animation has nothing to drive and was removed.");
                    continue;
                }
                foreach (var (fpr, shownWhenActive) in exclusions)
                {
                    // Polarity is per exclusion. A hiding chop inverts
                    // m_Enabled into isShown; a showing chop maps it
                    // straight across. Scale-factor curves mirror
                    // isShown directly for both.
                    var mapped = b.propertyName == "m_Enabled" && !shownWhenActive
                        ? Invert(curve)
                        : curve;
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

        public static void DeleteVrcComponents(BridgeContext ctx)
        {
            const string category = "Cleanup";

            // Seats get a named goodbye before the generic sweep.
            // The client's avatar whitelist has no seat type at all,
            // so this is a platform gap, not a conversion gap.
            // Counted here, after the strips, so GoGo
            // Loco's own stations (most of the wild count) vanish with GoGo instead of alarming
            // anyone. Uses the SDKBase type so SDK2-era stations are caught too.
            var stations = ctx.Target.GetComponentsInChildren<VRC.SDKBase.VRCStation>(true);
            if (stations.Length > 0)
            {
                var paths = stations.Take(4).Select(s => ctx.PathInTarget(s.transform));
                ctx.Report.Skipped(category,
                    $"{stations.Length} seat(s) removed — ChilloutVR avatars cannot host seats",
                    string.Join("; ", paths) + (stations.Length > 4 ? "; …" : "") +
                    " — VRChat's VRCStation lets other players sit on an avatar. ChilloutVR has " +
                    "no seat type on its avatar component whitelist (verified against the " +
                    "client), so there is nothing to convert these into: anyone used to sitting " +
                    "on this avatar can't here. Everything else about the object stays.");
            }

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
