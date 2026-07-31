#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Grafts the avatar's own locomotion animations into ChilloutVR's Locomotion/Emotes layer —
    /// the walking, crouching, crawling, falling and sitting the avatar actually shipped with,
    /// playing from the one layer on this platform that can both assert a body pose and yield it.
    ///
    /// Why grafting instead of keeping the VRChat Base layer live: merged above CVR's
    /// Locomotion/Emotes, a Base layer can only REPLACE that layer's output, never supplement it —
    /// and CVR's movement sliders and stance buttons are answered nowhere else. So the merged
    /// [Base] copy is masked off the body (AnimatorMerger.MaskMergedLayers), and this pass carries
    /// the ANIMATIONS across into the CCK's own states, where CVR's state machine decides when
    /// they play. The structure is ChilloutVR's; the art is the avatar's.
    ///
    /// The proxy discovery that shaped this pass: most VRChat avatars do not ship walking
    /// animations at all. Their locomotion trees reference VRChat's <c>proxy_*</c> placeholder
    /// clips, which the VRChat client swaps for its internal full-quality animations at runtime —
    /// the real walk lives in the client, not the avatar (measured on a heavily customized avatar
    /// whose "custom" standing tree was proxy_walk_forward/proxy_sprint_forward at the default
    /// positions). ChilloutVR's equivalent of the proxies is the CCK's own locomotion clips,
    /// already in place — so proxy children are skipped, and only animations the author actually
    /// authored are grafted.
    ///
    /// Clips are matched by BLEND-TREE POSITION, not by name: a child's velocity-space position
    /// says what it is (forward walk, backward run, strafe) regardless of naming convention, and
    /// the same classifier reads both platforms' trees — VRChat's in metres per second (walk 1.56,
    /// run 5.96), ChilloutVR's in normalized input (walk ring 0.4, run ring 1.0). Source clips are
    /// only referenced, never modified; AnimationSelfContainer copies them into RehomedAssets at
    /// the end of the pipeline like every other referenced clip.
    /// </summary>
    internal static class LocomotionGrafter
    {
        const string Category = "Animator";

        /// <summary>
        /// Clip -> loop-adjusted clone, per conversion. A grafted clip must carry the LOOP
        /// SETTING of the slot it lands in: the CCK's walk cycles loop and its states rely on
        /// that, while a custom clip straight off an avatar's FBX often doesn't — grafted as-is
        /// it plays once and freezes on the last frame, which testers see as "animations don't
        /// loop or finish". The source clip is never modified (it belongs to the source avatar);
        /// a clone is, and the asset saver persists it inside the output controller.
        /// </summary>
        static readonly Dictionary<(AnimationClip clip, bool loop), AnimationClip> LoopClones
            = new Dictionary<(AnimationClip, bool), AnimationClip>();

        /// <summary>Clip -> root-motion-free clone (or itself when it carried none).</summary>
        static readonly Dictionary<AnimationClip, AnimationClip> MotionStripped
            = new Dictionary<AnimationClip, AnimationClip>();

        /// <summary>Names of clips that had movement stripped, for the report.</summary>
        static readonly List<string> StrippedNames = new List<string>();

        /// <summary>
        /// Both clone caches MUST be per-conversion: a clone is persisted as a sub-asset of the
        /// output controller, and a cached clone reused by a SECOND conversion would try to live
        /// inside two assets at once. AnimatorMerger.Run calls this before any clip is prepared.
        /// </summary>
        internal static void ResetClones()
        {
            LoopClones.Clear();
            MotionStripped.Clear();
            StrippedNames.Clear();
        }

        /// <summary>
        /// The clip with every root-movement curve removed: humanoid RootT/RootQ and
        /// MotionT/MotionQ, and generic Transform curves on the avatar root itself.
        ///
        /// VRChat systems bake movement into animations because VRChat's avatars cannot move the
        /// player any other way — a copter takeoff climbs by animating the body upward. In
        /// ChilloutVR the CLIENT moves the player (flight, jumps, seats all its own), and the
        /// first-person camera rides the head bone — so a clip that also displaces the body
        /// shoves the wearer's camera around with no input. That logic is not converted; the
        /// muscles keep the pose, the game keeps the movement.
        /// </summary>
        internal static AnimationClip WithoutRootMotion(AnimationClip clip)
        {
            if (clip == null)
            {
                return null;
            }
            if (MotionStripped.TryGetValue(clip, out var done))
            {
                return done;
            }
            var doomed = new List<EditorCurveBinding>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (IsRootMovement(binding))
                {
                    doomed.Add(binding);
                }
            }
            if (doomed.Count == 0)
            {
                MotionStripped[clip] = clip;
                return clip;
            }
            var clone = UnityEngine.Object.Instantiate(clip);
            clone.name = clip.name;
            foreach (var binding in doomed)
            {
                AnimationUtility.SetEditorCurve(clone, binding, null);
            }
            MotionStripped[clip] = clone;
            StrippedNames.Add(clip.name);
            return clone;
        }

        static bool IsRootMovement(EditorCurveBinding binding)
        {
            if (!string.IsNullOrEmpty(binding.path))
            {
                return false; // a child bone's own animation is pose, not player movement
            }
            if (binding.type == typeof(Transform))
            {
                return true;
            }
            if (binding.type == typeof(Animator))
            {
                string p = binding.propertyName;
                return p.StartsWith("RootT.", StringComparison.Ordinal)
                    || p.StartsWith("RootQ.", StringComparison.Ordinal)
                    || p.StartsWith("MotionT.", StringComparison.Ordinal)
                    || p.StartsWith("MotionQ.", StringComparison.Ordinal);
            }
            return false;
        }

        /// <summary>Full preparation for a locomotion seat: movement stripped, loop matched.</summary>
        static AnimationClip Prepare(AnimationClip graft, Motion slotOriginal)
        {
            return LoopMatched(WithoutRootMotion(graft), slotOriginal);
        }

        static AnimationClip LoopMatched(AnimationClip graft, Motion slotOriginal)
        {
            if (!(slotOriginal is AnimationClip original) || graft == null)
            {
                return graft;
            }
            bool wantLoop = AnimationUtility.GetAnimationClipSettings(original).loopTime;
            if (AnimationUtility.GetAnimationClipSettings(graft).loopTime == wantLoop)
            {
                return graft;
            }
            if (LoopClones.TryGetValue((graft, wantLoop), out var cached))
            {
                return cached;
            }
            var clone = UnityEngine.Object.Instantiate(graft);
            clone.name = graft.name;
            var settings = AnimationUtility.GetAnimationClipSettings(clone);
            settings.loopTime = wantLoop;
            AnimationUtility.SetAnimationClipSettings(clone, settings);
            LoopClones[(graft, wantLoop)] = clone;
            return clone;
        }

        /// <summary>Direction-and-speed identity of a locomotion blend-tree child.</summary>
        enum Slot
        {
            Idle,
            WalkForward, RunForward,
            WalkBack, RunBack,
            WalkStrafe, RunStrafe,
            WalkForwardDiagonal, RunForwardDiagonal,
            WalkBackDiagonal, RunBackDiagonal
        }

        public static void Run(BridgeContext ctx, AnimatorController master)
        {
            // Clone caches are cleared by AnimatorMerger.Run BEFORE the Action transplant, which
            // also prepares clips through this class — clearing here would only break the
            // dedupe between the two.
            var cvrLayer = master.layers.FirstOrDefault(l => l != null && l.name == "Locomotion/Emotes");
            if (cvrLayer == null || cvrLayer.stateMachine == null)
            {
                return; // keep-GoGo mode removes the layer; nothing to graft into
            }
            var targets = CollectStates(cvrLayer.stateMachine);

            var grafts = new List<string>();
            int proxiesSkipped = 0;

            var baseController = ctx.Settings.convertBaseLayer
                ? SourceController(ctx, VRCAvatarDescriptor.AnimLayerType.Base)
                : null;
            if (baseController != null)
            {
                GraftStanceTrees(baseController, targets, grafts, ref proxiesSkipped);
                GraftJumpAndFall(baseController, targets, grafts, ref proxiesSkipped);
            }

            // Flight and swim poses ride ChilloutVR's OWN movement modes: the client answers
            // flight itself (world-permitting; keybind or double-jump; speed, sprint and world
            // multipliers all its own), raises the core Flying bool, and the CCK's LocFlying
            // state plays — so a VRChat "flight system" needs none of its speed logic converted,
            // only its pose put where this platform will show it. Decompiled:
            // BetterBetterCharacterController.ChangeFlight / HandleInputFlight, and
            // AvatarAnimatorManager.Flying = IsFlying() || UseZeroGravityControls.
            var poseSources = new List<AnimatorController>();
            if (baseController != null)
            {
                poseSources.Add(baseController);
            }
            if (ctx.Settings.convertActionLayer)
            {
                var action = SourceController(ctx, VRCAvatarDescriptor.AnimLayerType.Action);
                if (action != null)
                {
                    poseSources.Add(action);
                }
            }
            GraftMovementModePoses(poseSources, targets, grafts, ref proxiesSkipped);

            // The Sitting SPECIAL layer is descriptor-level content like the visemes — there is
            // no merge toggle for it, and its one useful product here is the sit pose itself.
            GraftSitting(ctx, targets, grafts, ref proxiesSkipped);

            if (grafts.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{grafts.Count} of the avatar's own locomotion animation(s) grafted into ChilloutVR's locomotion",
                    $"{string.Join("; ", grafts)}. These played from VRChat's Base/Action/Sitting playable " +
                    "layers, which cannot run as separate layers here — merged above ChilloutVR's " +
                    "Locomotion/Emotes they could only replace it, killing movement and stances. Instead the " +
                    "clips were moved into the matching states and blend-tree positions of ChilloutVR's OWN " +
                    "locomotion layer, matched by their velocity-space position rather than by name, and " +
                    "each grafted clip's loop setting is made to match the slot it fills — a cycle authored " +
                    "without looping would otherwise play once and freeze. The game still decides when to " +
                    "walk, fall, fly or sit; it now does so with this avatar's animations. A flight pose on " +
                    "LocFlying plays whenever ChilloutVR's own flight mode is active (keybind or double-jump, " +
                    "where the world allows it) — speed and movement are the client's, so a VRChat flight " +
                    "system's own speed logic is not needed and not converted.");
            }
            else if (proxiesSkipped > 0 && ctx.Settings.convertBaseLayer)
            {
                ctx.Report.Skipped(Category,
                    "Base locomotion is VRChat's built-in placeholder animations — nothing to carry over",
                    $"All {proxiesSkipped} animation(s) in the Base/Sitting locomotion trees are VRChat " +
                    "\"proxy\" clips (proxy_walk_forward and family). Proxies are stand-ins the VRChat " +
                    "client replaces with its internal animations at runtime — the real walk was never part " +
                    "of this avatar. ChilloutVR's equivalent is its own locomotion animation set, which the " +
                    "converted avatar already runs, so nothing is missing.");
            }

            if (StrippedNames.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"Movement baked into {StrippedNames.Count} animation(s) removed — ChilloutVR moves you itself",
                    $"{string.Join(", ", StrippedNames.Distinct())}. VRChat systems bake movement into their " +
                    "animations because a VRChat avatar cannot move the player any other way — a copter " +
                    "takeoff climbs by animating the body upward. Here the client owns all movement (flight, " +
                    "jumps, seats), and the first-person camera rides the head bone, so a clip that also " +
                    "displaces the body shoves the wearer around with no input. The root-movement curves were " +
                    "removed from the converted copies; the pose itself is untouched, and the game supplies " +
                    "the motion.");
            }
        }

        // ------------------------------------------------------------------ sources ----

        static AnimatorController SourceController(BridgeContext ctx, VRCAvatarDescriptor.AnimLayerType type)
        {
            foreach (var layer in ctx.SourceDescriptor.baseAnimationLayers)
            {
                if (layer.type == type && !layer.isDefault)
                {
                    return layer.animatorController as AnimatorController;
                }
            }
            return null;
        }

        /// <summary>
        /// A clip that is VRChat's, not the avatar's: the proxy_* placeholders and anything that
        /// ships inside the VRChat SDK packages. Grafting these would be re-shipping VRChat's
        /// low-quality "do not use" preview clips as the avatar's walk.
        /// </summary>
        static bool IsVrchatStock(AnimationClip clip)
        {
            if (clip == null)
            {
                return true;
            }
            if (clip.name.StartsWith("proxy_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            string path = AssetDatabase.GetAssetPath(clip) ?? "";
            return path.IndexOf("com.vrchat.", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------ stance trees ----

        static void GraftStanceTrees(AnimatorController source,
            Dictionary<string, AnimatorState> targets, List<string> grafts, ref int proxiesSkipped)
        {
            // CCK state name -> which source states qualify as that stance. "Lying" is what
            // pose-style controllers call prone.
            var stances = new (string cvrState, string[] nameTokens)[]
            {
                ("Standard Locomotion", new[] { "stand" }),
                ("Crouching Locomotion", new[] { "crouch" }),
                ("Prone Locomotion", new[] { "prone", "lying", "lie" }),
            };

            foreach (var (cvrStateName, tokens) in stances)
            {
                if (!targets.TryGetValue(cvrStateName, out var cvrState) || !(cvrState.motion is BlendTree cvrTree))
                {
                    continue; // a future CCK reshaped its layer — degrade to no graft, not to a wrong one
                }
                foreach (var state in AllStates(source))
                {
                    string name = state.name.ToLowerInvariant();
                    if (!tokens.Any(t => name.Contains(t)))
                    {
                        continue;
                    }
                    if (state.motion is BlendTree sourceTree && IsVelocityTree(sourceTree))
                    {
                        GraftTree(sourceTree, cvrTree, cvrStateName, grafts, ref proxiesSkipped);
                        break; // first velocity tree per stance wins; duplicates are copies of it
                    }
                    if (state.motion is AnimationClip pose)
                    {
                        if (!IsVrchatStock(pose))
                        {
                            // A pose-style stance (a single custom clip, no movement tree) still
                            // has an authored idle — graft it into the tree's centre.
                            if (ReplaceAt(cvrTree, Slot.Idle, pose))
                            {
                                grafts.Add($"{cvrStateName} idle ← \"{pose.name}\"");
                            }
                            break;
                        }
                        proxiesSkipped++;
                    }
                    // Anything else (a non-velocity tree, an empty state) is not this stance's
                    // locomotion — keep looking at the remaining candidates.
                }
            }
        }

        static bool IsVelocityTree(BlendTree tree)
        {
            return (tree.blendType == BlendTreeType.FreeformDirectional2D
                    || tree.blendType == BlendTreeType.SimpleDirectional2D
                    || tree.blendType == BlendTreeType.FreeformCartesian2D)
                && tree.blendParameter == "VelocityX"
                && tree.blendParameterY == "VelocityZ";
        }

        static void GraftTree(BlendTree sourceTree, BlendTree cvrTree, string label,
            List<string> grafts, ref int proxiesSkipped)
        {
            // Both trees run through the same classifier: direction from the child's angle,
            // speed ring from its magnitude relative to the others in its direction. The units
            // differ (m/s vs normalized input) but the GEOMETRY is the same language.
            var sourcePicks = new Dictionary<Slot, AnimationClip>();
            foreach (var (slot, clip) in ClassifyClips(sourceTree))
            {
                if (IsVrchatStock(clip))
                {
                    proxiesSkipped++;
                    continue;
                }
                // First pick per slot wins; later duplicates (mirrored diagonals reusing one
                // clip) would be the same clip anyway.
                if (!sourcePicks.ContainsKey(slot))
                {
                    sourcePicks[slot] = clip;
                }
            }
            foreach (var pair in sourcePicks)
            {
                if (ReplaceAt(cvrTree, pair.Key, pair.Value))
                {
                    grafts.Add($"{label} {Describe(pair.Key)} ← \"{pair.Value.name}\"");
                }
            }
        }

        /// <summary>Every direct clip child of the tree, tagged with its slot.</summary>
        static IEnumerable<(Slot slot, AnimationClip clip)> ClassifyClips(BlendTree tree)
        {
            var children = tree.children;
            float maxMag = 0f;
            foreach (var child in children)
            {
                maxMag = Mathf.Max(maxMag, child.position.magnitude);
            }
            if (maxMag <= 0f)
            {
                yield break;
            }

            // Walk/run is decided per DIRECTION: VRChat's back-run (2.1 m/s) is slower than its
            // forward-walk-adjacent jog (3.4), so a global speed split misfiles it.
            var byDirection = new Dictionary<int, List<float>>();
            foreach (var child in children)
            {
                int direction = DirectionOf(child.position, maxMag);
                if (!byDirection.TryGetValue(direction, out var mags))
                {
                    byDirection[direction] = mags = new List<float>();
                }
                mags.Add(child.position.magnitude);
            }

            foreach (var child in children)
            {
                if (!(child.motion is AnimationClip clip))
                {
                    continue; // nested trees have their own parameter space; positions don't carry
                }
                var slot = SlotOf(child.position, maxMag, byDirection);
                if (slot.HasValue)
                {
                    yield return (slot.Value, clip);
                }
            }
        }

        /// <summary>0 idle, 1 forward, 2 forward-diagonal, 3 strafe, 4 back-diagonal, 5 back.</summary>
        static int DirectionOf(Vector2 position, float maxMag)
        {
            if (position.magnitude < 0.15f * maxMag)
            {
                return 0;
            }
            float angle = Vector2.Angle(Vector2.up, new Vector2(Mathf.Abs(position.x), position.y));
            if (angle < 22.5f) return 1;
            if (angle < 67.5f) return 2;
            if (angle < 112.5f) return 3;
            if (angle < 157.5f) return 4;
            return 5;
        }

        static Slot? SlotOf(Vector2 position, float maxMag, Dictionary<int, List<float>> byDirection)
        {
            int direction = DirectionOf(position, maxMag);
            if (direction == 0)
            {
                return Slot.Idle;
            }
            var mags = byDirection[direction];
            float slowest = mags.Min(), fastest = mags.Max();
            float mag = position.magnitude;
            bool run;
            if (Mathf.Approximately(slowest, fastest))
            {
                // A single speed in this direction (crouch/prone trees): whether it is the walk
                // or the run is judged against the tree's overall top speed.
                run = mag >= 0.55f * maxMag;
            }
            else if (Mathf.Approximately(mag, slowest))
            {
                run = false;
            }
            else if (Mathf.Approximately(mag, fastest))
            {
                run = true;
            }
            else
            {
                return null; // a middle speed (VRChat's jog) — CVR has no ring for it
            }
            switch (direction)
            {
                case 1: return run ? Slot.RunForward : Slot.WalkForward;
                case 2: return run ? Slot.RunForwardDiagonal : Slot.WalkForwardDiagonal;
                case 3: return run ? Slot.RunStrafe : Slot.WalkStrafe;
                case 4: return run ? Slot.RunBackDiagonal : Slot.WalkBackDiagonal;
                default: return run ? Slot.RunBack : Slot.WalkBack;
            }
        }

        /// <summary>
        /// Replaces every CVR tree child occupying <paramref name="slot"/> with the clip —
        /// every one, because both platforms reuse a single clip across mirrored ± positions
        /// (the CCK's forward-diagonal walk sits at both (0.25, 0.25) and (-0.25, 0.25)).
        /// Single-ring CVR trees (crouch, prone: one speed per direction) classify their sole
        /// child as run — its magnitude IS the direction's top speed — so a two-speed source
        /// stance grafts its faster clip there and the walk pick simply finds no seat.
        /// </summary>
        static bool ReplaceAt(BlendTree cvrTree, Slot slot, AnimationClip clip)
        {
            var children = cvrTree.children;
            float maxMag = 0f;
            foreach (var child in children)
            {
                maxMag = Mathf.Max(maxMag, child.position.magnitude);
            }
            var byDirection = new Dictionary<int, List<float>>();
            foreach (var child in children)
            {
                int direction = DirectionOf(child.position, maxMag);
                if (!byDirection.TryGetValue(direction, out var mags))
                {
                    byDirection[direction] = mags = new List<float>();
                }
                mags.Add(child.position.magnitude);
            }
            bool replaced = false;
            for (int i = 0; i < children.Length; i++)
            {
                if (SlotOf(children[i].position, maxMag, byDirection) != slot)
                {
                    continue;
                }
                var use = Prepare(clip, children[i].motion);
                if (children[i].motion != use)
                {
                    children[i].motion = use;
                    replaced = true;
                }
            }
            if (replaced)
            {
                cvrTree.children = children;
                EditorUtility.SetDirty(cvrTree);
            }
            return replaced;
        }

        static string Describe(Slot slot)
        {
            switch (slot)
            {
                case Slot.Idle: return "idle";
                case Slot.WalkForward: return "walk";
                case Slot.RunForward: return "run";
                case Slot.WalkBack: return "walk back";
                case Slot.RunBack: return "run back";
                case Slot.WalkStrafe: return "strafe";
                case Slot.RunStrafe: return "strafe run";
                case Slot.WalkForwardDiagonal: return "diagonal walk";
                case Slot.RunForwardDiagonal: return "diagonal run";
                case Slot.WalkBackDiagonal: return "back-diagonal walk";
                default: return "back-diagonal run";
            }
        }

        // ------------------------------------------------------------- jump & fall ----

        static void GraftJumpAndFall(AnimatorController source,
            Dictionary<string, AnimatorState> targets, List<string> grafts, ref int proxiesSkipped)
        {
            // VRChat state name (first match wins) -> the CCK state that plays the same moment.
            var moments = new (string cvrState, string[] sourceNames)[]
            {
                ("JumpStart", new[] { "smallhop", "small hop" }),
                ("JumpAir", new[] { "short fall", "shortfall", "long fall", "longfall" }),
                ("JumpLand", new[] { "quickland", "quick land", "hardland", "hard land", "land" }),
            };
            foreach (var (cvrStateName, sourceNames) in moments)
            {
                if (!targets.TryGetValue(cvrStateName, out var cvrState))
                {
                    continue;
                }
                foreach (string wanted in sourceNames)
                {
                    var state = AllStates(source).FirstOrDefault(s =>
                        s.name.Replace("_", " ").ToLowerInvariant() == wanted
                        && s.motion is AnimationClip);
                    if (state == null)
                    {
                        continue;
                    }
                    var clip = (AnimationClip)state.motion;
                    if (IsVrchatStock(clip))
                    {
                        proxiesSkipped++;
                        break; // the stock clip in the expected slot; later names are fallbacks
                    }
                    var use = Prepare(clip, cvrState.motion);
                    if (cvrState.motion != use)
                    {
                        cvrState.motion = use;
                        EditorUtility.SetDirty(cvrState);
                        grafts.Add($"{cvrStateName} ← \"{clip.name}\"");
                    }
                    break;
                }
            }
        }

        // ----------------------------------------------------------------- sitting ----

        static void GraftSitting(BridgeContext ctx,
            Dictionary<string, AnimatorState> targets, List<string> grafts, ref int proxiesSkipped)
        {
            if (!targets.TryGetValue("Sitting", out var cvrSitting))
            {
                return;
            }
            AnimatorController sitting = null;
            foreach (var layer in ctx.SourceDescriptor.specialAnimationLayers)
            {
                if (layer.type == VRCAvatarDescriptor.AnimLayerType.Sitting && !layer.isDefault)
                {
                    sitting = layer.animatorController as AnimatorController;
                }
            }
            if (sitting == null)
            {
                return;
            }
            // The layer's states are tracking plumbing sharing one or two pose clips; the pose
            // used by the most states is the sit. A tie means two DIFFERENT authored sits with no
            // way to pick — leave the CCK's.
            var votes = new Dictionary<AnimationClip, int>();
            foreach (var state in AllStates(sitting))
            {
                if (state.motion is AnimationClip clip)
                {
                    if (IsVrchatStock(clip))
                    {
                        proxiesSkipped++;
                        continue;
                    }
                    votes[clip] = votes.TryGetValue(clip, out int n) ? n + 1 : 1;
                }
            }
            if (votes.Count == 0)
            {
                return;
            }
            int best = votes.Values.Max();
            var winners = votes.Where(v => v.Value == best).Select(v => v.Key).ToList();
            if (winners.Count != 1)
            {
                return;
            }
            var use = Prepare(winners[0], cvrSitting.motion);
            if (cvrSitting.motion != use)
            {
                cvrSitting.motion = use;
                EditorUtility.SetDirty(cvrSitting);
                grafts.Add($"Sitting ← \"{winners[0].name}\"");
            }
        }

        // -------------------------------------------------- flight & swim poses ----

        /// <summary>
        /// Puts a flight (or swim) pose on the state ChilloutVR's own movement mode plays.
        /// VRChat has no flight, so avatars fake it with seat tricks and locomotion replacements
        /// carrying their own speed logic; ChilloutVR flies natively — none of that machinery is
        /// needed, only the pose, on LocFlying, where the client will show it whenever the
        /// wearer actually flies.
        /// </summary>
        static void GraftMovementModePoses(List<AnimatorController> sources,
            Dictionary<string, AnimatorState> targets, List<string> grafts, ref int proxiesSkipped)
        {
            var modes = new (string cvrState, string[] tokens)[]
            {
                ("LocFlying", new[] { "fly", "flight", "flying", "hover", "glide", "copter" }),
                ("Swimming", new[] { "swim" }),
            };
            foreach (var (cvrStateName, tokens) in modes)
            {
                if (!targets.TryGetValue(cvrStateName, out var cvrState))
                {
                    continue;
                }
                foreach (var source in sources)
                {
                    AnimatorState found = null;
                    foreach (var state in AllStates(source))
                    {
                        string name = state.name.ToLowerInvariant();
                        if (!tokens.Any(t => name.Contains(t)) || !(state.motion is AnimationClip))
                        {
                            continue;
                        }
                        if (IsVrchatStock((AnimationClip)state.motion))
                        {
                            proxiesSkipped++;
                            continue;
                        }
                        found = state;
                        break;
                    }
                    if (found == null)
                    {
                        continue;
                    }
                    var clip = (AnimationClip)found.motion;
                    var use = Prepare(clip, cvrState.motion);
                    if (cvrState.motion != use)
                    {
                        cvrState.motion = use;
                        EditorUtility.SetDirty(cvrState);
                        grafts.Add($"{cvrStateName} ← \"{clip.name}\" (from \"{found.name}\")");
                    }
                    break;
                }
            }
        }

        // ----------------------------------------------------------------- walkers ----

        static Dictionary<string, AnimatorState> CollectStates(AnimatorStateMachine root)
        {
            var result = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            foreach (var state in AllStates(root))
            {
                if (!result.ContainsKey(state.name))
                {
                    result[state.name] = state;
                }
            }
            return result;
        }

        static IEnumerable<AnimatorState> AllStates(AnimatorController controller)
        {
            foreach (var layer in controller.layers)
            {
                foreach (var state in AllStates(layer.stateMachine))
                {
                    yield return state;
                }
            }
        }

        static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine machine)
        {
            if (machine == null)
            {
                yield break;
            }
            var stack = new Stack<AnimatorStateMachine>();
            var seen = new HashSet<AnimatorStateMachine>();
            stack.Push(machine);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null || !seen.Add(current))
                {
                    continue;
                }
                foreach (var child in current.states)
                {
                    if (child.state != null)
                    {
                        yield return child.state;
                    }
                }
                foreach (var sub in current.stateMachines)
                {
                    stack.Push(sub.stateMachine);
                }
            }
        }

        static int CountClips(Motion motion)
        {
            if (motion is AnimationClip)
            {
                return 1;
            }
            if (motion is BlendTree tree)
            {
                int n = 0;
                foreach (var child in tree.children)
                {
                    n += CountClips(child.motion);
                }
                return n;
            }
            return 0;
        }
    }
}
#endif
