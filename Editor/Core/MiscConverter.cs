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
        /// <summary>
        /// Turns ON a particle emitter that was authored OFF and switched on only by animation.
        ///
        /// Reported in the wild: a headpat effect nobody but the wearer could see, on an avatar
        /// whose nose-boop effect — same contact, same kind of tree, same everything — worked for
        /// everyone. The two were built differently, and that was the whole difference:
        ///
        ///   nose boop  clips animate ONLY m_IsActive; the emitter is enabled in the prefab
        ///   headpat    clips animate m_IsActive AND EmissionModule.enabled; emitter authored OFF
        ///
        /// Switching a GameObject on is something every client's animator does. Animating a
        /// ParticleSystem MODULE property is not the same kind of write, and where it fails to
        /// land the object dutifully activates and emits nothing — invisible, while looking
        /// perfectly correct on the rare occasion it does show.
        ///
        /// So this removes the dependency rather than chasing it: where a clip animates emission
        /// on an object whose emitter is authored off, AND the same clip already drives that
        /// object's m_IsActive, the emitter is enabled for good and the object's own active state
        /// gates the effect — exactly the shape that already works.
        ///
        /// THE m_IsActive REQUIREMENT IS THE SAFETY, not a convenience. An emitter enabled on an
        /// object that nothing switches off would simply run forever. Requiring the clip to drive
        /// m_IsActive means there is already something turning it off; the measured avatar's
        /// "Headpat OFF" sets m_IsActive 0 and the object rests inactive, so nothing emits at rest.
        ///
        /// Only EMISSION is touched. Other modules were left alone deliberately: emission off is
        /// the one that means "nothing comes out at all", and the rest are refinements whose
        /// failure is visible but not fatal. They are counted and reported instead of guessed at.
        /// </summary>
        public static void EnableAnimatedParticleEmitters(BridgeContext ctx)
        {
            var controller = ctx.MergedController;
            if (controller == null || ctx.Target == null)
            {
                return;
            }

            // Which paths each clip drives m_IsActive on — the gate that makes enabling safe.
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
                    var child = ctx.Target.transform.Find(binding.path);
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

                    // And take the curve OUT, which is the half this was missing. Forcing the
                    // component on is undone the moment the OFF clip plays and writes the module
                    // back to false — which is at rest, always — so the emitter was never
                    // "on permanently" the way the report claimed. The object's own m_IsActive
                    // gates the effect; a module curve alongside it can only fight that.
                    //
                    // ONLY on a clip this conversion owns. Editing a clip in place reaches
                    // whatever asset it really is, and a source avatar's clips — or worse, an SDK
                    // package's — are shared by everything that references them. A pass that
                    // stripped curves without this check once emptied a VRChat SDK proxy clip for
                    // the whole project.
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

        /// <summary>
        /// Names particle systems rendering on Unity's built-in default material.
        ///
        /// "Blank coloured squares" is what that looks like in game, and it is invisible in the
        /// editor unless someone thinks to click the renderer. Reported in the wild on an avatar
        /// whose nose-boop effect drew as plain quads: its "Buffer Particle" pointed at Unity's
        /// Default-ParticleSystem, and it had done so in the SOURCE avatar all along — conversion
        /// carried across exactly what was there. Establishing that took a hunt through GUIDs that
        /// one line of report would have answered.
        ///
        /// So this accuses nobody and fixes nothing: it says which systems are on the default
        /// material, so the author can decide whether that was intended. A missing material counts
        /// too — same symptom, same question.
        ///
        /// Matched by NAME rather than by asset path, deliberately. The rehoming pass has already
        /// copied the material into this conversion's own folder by the time this runs, so the
        /// built-in path is gone; "Default-ParticleSystem" is Unity's fixed name for it and
        /// survives the copy.
        /// </summary>
        /// <summary>
        /// True only for a clip this conversion created and owns, which is the one kind safe to
        /// edit in place.
        ///
        /// Everything else is shared by reference: a source avatar's clips belong to the avatar,
        /// and an SDK package's belong to every project that imported it. A pass that stripped
        /// curves without asking this question reached into the VRChat SDK's own
        /// proxy_hands_idle.anim and emptied it — for the whole project, silently, so every avatar
        /// converted afterwards lost its hand poses. An unsaved clip is ours by construction (it
        /// has been built in memory this run); a saved one has to live under the output folder.
        /// </summary>
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
            int clamped = 0, flattened = 0;
            var notes = new List<string>();
            var flat = new List<string>();
            var nearby = new List<string>();
            foreach (var source in ctx.Target.GetComponentsInChildren<AudioSource>(true))
            {
                bool changed = false;

                // 2D audio is not merely "unpositioned" here, it is DROPPED. ChilloutVR decides
                // whether to spatialize from the blend itself — SharedFilter.ProcessAudioSource
                // sets spatialize = spatialBlend >= 1 — and a source that fails that test is not
                // handed to Steam Audio, so it can go unheard by everyone but the wearer. Which is
                // exactly how a 2D sound presents: it plays perfectly for you and never for them.
                // An avatar sound is attached to a body in a room and has no business being flat.
                if (source.spatialBlend < 1f)
                {
                    source.spatialBlend = 1f;
                    changed = true;
                    flattened++;
                    if (flat.Count < 6)
                    {
                        flat.Add(source.gameObject.name);
                    }
                }
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
                // Not changed — the author's reach is the author's call. But a sound that stops
                // carrying inside arm's length is worth saying out loud, because it presents as
                // "nobody else can hear it" and looks like a conversion fault.
                if (source.maxDistance < 5f && nearby.Count < 6)
                {
                    nearby.Add($"{source.gameObject.name} ({source.maxDistance:0.#} m)");
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

            if (flattened > 0)
            {
                ctx.Report.Converted("Audio",
                    $"{flattened} flat (2D) audio source(s) made positional",
                    $"On: {string.Join(", ", flat)}{(flattened > flat.Count ? ", …" : "")} — these were " +
                    "authored with Spatial Blend below fully 3D. ChilloutVR decides whether to " +
                    "spatialize a source from that blend alone, and one that does not reach fully 3D " +
                    "is never handed to the spatializer — so it plays for the wearer and can be " +
                    "silent for everyone else, which reads as a broken sound rather than a flat one. " +
                    "An avatar's sound belongs to a body in a room, so the blend is set to 3D.");
            }

            if (nearby.Count > 0)
            {
                ctx.Report.Approximated("Audio",
                    $"{nearby.Count} audio source(s) stop carrying within a few metres",
                    $"{string.Join(", ", nearby)} — left exactly as the author set them, because how " +
                    "far a sound should carry is a decision rather than a defect. Worth knowing all " +
                    "the same: past that distance the sound is silent, so a listener standing a normal " +
                    "conversational distance away hears nothing while you hear it perfectly. If one of " +
                    "these is meant to be noticed by the person setting it off, raise its Max Distance " +
                    "on the AudioSource.");
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
        /// How far past the avatar's own silhouette a box reaches, as a fraction of its height.
        /// 0.3 is half a metre on a 1.65 m avatar — enough for hair, a skirt or a tail to swing
        /// into — and it scales, where a flat 0.5 m would be most of the box on a 40 cm chibi
        /// and nothing at all on a five-metre dragon.
        /// </summary>
        const float BoundsPaddingFraction = 0.3f;

        /// <summary>
        /// Unity culls a skinned mesh by its AUTHORED bounding box, not by where animation,
        /// physics or cloth actually put the vertices — the box is baked from the bind pose and
        /// never follows. The moment the stale box leaves the camera frustum the whole mesh blinks
        /// out: classically at screen edges, or for another player looking from the side.
        ///
        /// The fix is a box that is generous but SHAPED LIKE THE AVATAR. This used to be a cube of
        /// the avatar's eye height in every direction centred on each mesh's root bone, which was
        /// generous and nothing else: on a 1.7 m avatar it is a 3.4 m cube reaching as far below
        /// the hips as above the head, most of it empty. Now every mesh gets the avatar's own
        /// measured volume plus <see cref="BoundsPaddingFraction"/> of its height, which is both
        /// smaller and better placed.
        ///
        /// It now SHRINKS boxes that were authored larger, where before those were left alone.
        /// That direction is the one that can cause culling rather than prevent it, so it is
        /// deliberate: the envelope is the whole avatar plus a swing margin, and a mesh with
        /// vertices outside that is a prop that flies away from the body — rare, and visible
        /// immediately if it happens. Only wearing the avatar in ChilloutVR can confirm it.
        /// </summary>
        static void NormalizeSkinnedBounds(BridgeContext ctx)
        {
            float height = Mathf.Max(AvatarScalerInjector.MeasureHeight(ctx), 1.5f);

            if (!MeasureAvatarVolume(ctx.Target, out var envelope)
                // A measurement shorter than half the avatar means the geometry did not give a
                // usable answer — a mesh with broken bounds, an armature with no skin. Shrinking
                // every box to THAT would make the avatar disappear, which is the failure this
                // pass exists to prevent, so it declines rather than guesses.
                || envelope.size.y < height * 0.5f)
            {
                ctx.Report.Warning("Meshes", "Bounding boxes left as the avatar had them",
                    "The avatar's own volume could not be measured from its meshes, so there was nothing " +
                    "trustworthy to size the culling boxes against. If meshes vanish at the edge of the " +
                    "screen in game, that is what this would have fixed — please report the avatar.");
                return;
            }

            envelope.Expand(height * BoundsPaddingFraction * 2f);   // Expand takes a diameter

            int changed = 0;
            foreach (var renderer in ctx.Target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // localBounds is expressed in the ROOT BONE's space, so the envelope — measured in
                // world metres — has to be carried into that space before it means anything here.
                // Without this the same number meant 1.5 m on an ordinary rig, 1.5 cm on a bone
                // scaled to 0.01, and 150 m on one scaled to 100 (Second Life conversions run
                // around 100x). Reported once as "sometimes too small or too big", which is
                // exactly what a unit mismatch looks like from outside.
                var space = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
                var wanted = ToLocalBounds(space, envelope);

                if (Approximately(renderer.localBounds, wanted))
                {
                    continue;
                }
                renderer.localBounds = wanted;
                EditorUtility.SetDirty(renderer);
                changed++;
            }

            if (changed > 0)
            {
                ctx.Report.Converted("Meshes",
                    $"{changed} skinned mesh bounding box(es) resized to the avatar — " +
                    $"{envelope.size.x:0.##} × {envelope.size.y:0.##} × {envelope.size.z:0.##} m",
                    "Unity culls a skinned mesh by its authored bind-pose box, not by where animation, physics " +
                    "or cloth actually put the vertices — so a mesh can vanish at screen edges while plainly on " +
                    "camera. Each box is now the avatar's own measured volume with " +
                    $"{height * BoundsPaddingFraction:0.##} m of clearance around it for hair, skirts and tails " +
                    "to swing into, placed where the avatar actually is rather than centred on each mesh's root " +
                    "bone. Boxes that were larger than this are brought down to it as well.");
            }
        }

        /// <summary>
        /// The space the avatar occupies, in world metres, measured from the bones that skin it.
        ///
        /// Three sources were tried on a real avatar (BHFBunny, 26 skinned meshes, root scaled 2×)
        /// and only one of them is trustworthy:
        ///
        ///   <c>Renderer.bounds</c> / <c>localBounds</c> — circular. On a skinned mesh that IS the
        ///   culling box this pass exists to correct, so it hands back whatever wrong answer the
        ///   avatar arrived with. Measured 4.05 × 4.99 × 4.16 m: the bad box, read back.
        ///
        ///   <c>sharedMesh.bounds</c> — a different field, and not stale, but it is expressed in
        ///   the mesh's own authoring space, which is NOT the root bone's and NOT metres. On that
        ///   avatar the whole body mesh reads 0.11 × 0.13 × 0.13, because the scale to world lives
        ///   in the bindposes. Mapping it through the root bone gave 1.40 × 0.41 × 1.72 — a
        ///   40 cm tall four-metre avatar — and mapping it through a bindpose still came out at
        ///   the wrong scale.
        ///
        ///   The BONES — 1.33 × 4.02 × 1.31 m, which is that avatar. Bone positions are read
        ///   straight from the transforms, so there is no stale field and no space to convert out
        ///   of. Every skinning bone counts, and only skinning bones: an ordinary child transform
        ///   might be a world-space prop parked at the origin or an effect anchor, and one of
        ///   those would swallow the measurement whole. What deforms the mesh is the mesh's size.
        ///
        /// The skin does reach past its bones — a wide skirt, a shoulder pad. That is what the
        /// padding at the call site is for, and it is the reason the padding is generous.
        /// </summary>
        static bool MeasureAvatarVolume(GameObject root, out Bounds world)
        {
            // A local rather than the out parameter directly: C# won't let a local function
            // capture an out parameter.
            var measured = new Bounds();
            bool any = false;

            void Add(Vector3 point)
            {
                if (any)
                {
                    measured.Encapsulate(point);
                }
                else
                {
                    measured = new Bounds(point, Vector3.zero);
                    any = true;
                }
            }

            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (var bone in skinned.bones)
                {
                    if (bone != null)
                    {
                        Add(bone.position);
                    }
                }
                // A skinned mesh with no bone list is rare but legal; its root bone still says
                // where it is, and without this such a mesh would contribute nothing at all.
                if (skinned.bones.Length == 0 && skinned.rootBone != null)
                {
                    Add(skinned.rootBone.position);
                }
            }

            world = measured;
            return any;
        }

        /// <summary>
        /// An axis-aligned box through a matrix, via its eight corners.
        ///
        /// The result is the AABB of the transformed corners, which under rotation is slightly
        /// larger than the true bound of the contents. That is the safe direction here: too large
        /// draws a mesh that was about to leave the screen, too small blinks it out.
        /// </summary>
        static Bounds TransformBounds(Matrix4x4 matrix, Bounds local)
        {
            var min = local.min;
            var max = local.max;
            var result = new Bounds(matrix.MultiplyPoint3x4(min), Vector3.zero);
            for (int i = 1; i < 8; i++)
            {
                result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z)));
            }
            return result;
        }

        static Bounds ToLocalBounds(Transform space, Bounds world) =>
            TransformBounds(space.worldToLocalMatrix, world);

        /// <summary>
        /// Millimetre tolerance, so a box that is already right isn't rewritten every conversion.
        /// Comparing Bounds with == would call a float a hair off "different" and dirty every
        /// renderer on the avatar for nothing.
        /// </summary>
        static bool Approximately(Bounds a, Bounds b) =>
            (a.center - b.center).sqrMagnitude < 1e-6f && (a.extents - b.extents).sqrMagnitude < 1e-6f;

        /// <summary>Test seam — HeadChopCurveTest asserts the per-type m_Enabled polarity.</summary>
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

        /// <summary>
        /// VRChat toggles Head Chop by animating the VRCHeadChop component (scale factor 1=shown,
        /// 0=hidden). CVR's FPRExclusion exposes that as an animatable `isShown` bool, so we clone
        /// the driving clips and rebind the head-chop curves onto each FPRExclusion's isShown —
        /// scale factor maps straight across (1→shown, 0→hidden); a `m_Enabled` curve is inverted.
        /// </summary>
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
                    // The chop this drove produced no exclusion (every target bone skipped, or a
                    // fractional scale factor). Silently dead until now — say so instead.
                    ctx.Report.Warning("Head chop",
                        $"\"{clip.name}\" animated a head chop that was not converted",
                        $"{b.path} ({b.propertyName}) — the head chop there was skipped, so this " +
                        "animation has nothing to drive and was removed.");
                    continue;
                }
                foreach (var (fpr, shownWhenActive) in exclusions)
                {
                    // Polarity is PER EXCLUSION, not global. A hiding chop (scale 0) is INACTIVE
                    // by default, so enabling it hides: m_Enabled inverts into isShown. A showing
                    // chop (scale 1, the keep-my-accessory-visible-in-first-person idiom) is the
                    // opposite: enabling it SHOWS, so m_Enabled maps straight across — the old
                    // unconditional inversion played those exactly backwards. Scale-factor curves
                    // mirror isShown directly for both types (1 = shown, 0 = hidden).
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

        /// <summary>Removes every remaining VRC component. Run this last.</summary>
        public static void DeleteVrcComponents(BridgeContext ctx)
        {
            const string category = "Cleanup";

            // Seats get a named goodbye before the generic sweep eats them. VRCStation is the
            // sit-on-me chair — 102 across the wild census — and the decompiled client's avatar
            // whitelist has no seat type at all, so this is a platform gap, not a conversion
            // gap: the honest ceiling is saying so. Counted HERE, after the strips, so GoGo
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
