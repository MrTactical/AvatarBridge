#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ABI.CCK.Components;

namespace AvatarBridge
{
    /// <summary>
    /// Merges the selected VRChat playable layers (Base/Additive/Gesture/Action/FX) into a
    /// single ChilloutVR animator controller built on top of the CCK's default
    /// AvatarAnimator, then rewrites everything VRC-specific:
    ///
    ///  - GestureLeft/GestureRight int values -> CVR float values (with range conditions)
    ///  - GestureLeftWeight/RightWeight       -> fed by a CVRParameterStream (trigger value)
    ///  - VRC parameter names                 -> CVR core names (Viseme -> VisemeIdx, ...)
    ///  - non-synced parameters               -> "#" prefix (CVR local-only convention)
    ///  - menu Buttons                        -> plain toggles (CVR has no momentary control)
    ///  - VRCAvatarParameterDriver            -> CCK AnimatorDriver
    ///  - VRC built-in avatar masks           -> equivalent generated masks
    /// </summary>
    public static class AnimatorMerger
    {
        const string Category = "Animator";
        const int MaxConditionBranches = 64;

        static readonly string[] CckAnimatorPaths =
        {
            "Assets/CVR.CCK/Assets/Avatar/Animations/AvatarAnimator.controller", // CCK 4.x
            "Assets/ABI.CCK/Animations/AvatarAnimator.controller"                // CCK 3.x
        };

        // VRChat parameter -> ChilloutVR core parameter.
        // GestureLeftWeight/RightWeight are NOT renamed: a CVRParameterStream feeds them
        // from the real trigger values instead (see CreateParameterStreams).
        static readonly Dictionary<string, string> ParameterRenameMap = new Dictionary<string, string>
        {
            { "Viseme", "VisemeIdx" },
            { "Voice", "VisemeLoudness" },
            { "Seated", "Sitting" },
            { "InStation", "Sitting" },
            { "IsOnFriendsList", "IsFriend" }
        };

        // Parameters ChilloutVR drives itself; these must never be renamed or prefixed.
        internal static readonly HashSet<string> CvrCoreParameters = new HashSet<string>
        {
            "MovementX", "MovementY", "Grounded", "Emote", "CancelEmote",
            "GestureLeft", "GestureRight", "GestureLeftIdx", "GestureRightIdx",
            "Toggle", "Sitting", "Crouching", "Prone", "Flying", "Swimming",
            "IsLocal", "DistanceTo", "VisemeIdx", "VisemeLoudness", "IsFriend",
            "VelocityX", "VelocityY", "VelocityZ", "AFK"
        };

        // Parameters a CVRParameterStream feeds from the game (see CreateParameterStreams).
        // These are deliberately kept out of the "#" local prefix in RenamePass: the stream only
        // runs on the wearer's copy, so replication is the sole way its value reaches anyone else.
        static readonly HashSet<string> StreamFedParameters = new HashSet<string>
        {
            "GestureLeftWeight", "GestureRightWeight", "MuteSelf", "VRMode",
            "Upright", "TrackingType"
        };

        /// <summary>
        /// True if ChilloutVR supplies this parameter's value itself — core avatar state
        /// (gestures, locomotion, visemes) or something fed by a CVRParameterStream.
        ///
        /// VRChat avatars often declare these as synced expression parameters so their FX can
        /// read them. They must never become menu controls: the game overwrites the value
        /// every frame, so the control does nothing while still costing sync budget.
        /// Takes the VRChat-side name, since this is asked before the rename pass.
        /// </summary>
        internal static bool IsGameDrivenParameter(string vrcParameterName)
        {
            if (string.IsNullOrEmpty(vrcParameterName))
            {
                return false;
            }
            string bare = vrcParameterName.TrimStart('#');
            string mapped = ParameterRenameMap.TryGetValue(bare, out var renamed) ? renamed : bare;
            return CvrCoreParameters.Contains(mapped)
                   || StreamFedParameters.Contains(bare)
                   || GestureMap.GestureWeightParameters.Contains(bare);
        }

        // CVR drives these to non-zero at runtime; matching defaults avoids startup glitches.
        // Anything CVR does NOT drive belongs in UnsupportedBuiltInDefaults instead — the scale
        // and eye-height entries used to be listed here as well as in the unsupported set below,
        // which are contradictory claims about the same parameter.
        static readonly Dictionary<string, float> NonZeroDefaults = new Dictionary<string, float>
        {
            { "Grounded", 1f }
        };

        // VRC built-ins with no CVR equivalent; they stay as frozen local parameters.
        // (MuteSelf/VRMode/GestureWeights/Upright/TrackingType are fed by a CVRParameterStream
        // instead, so they are live rather than frozen — see StreamFedParameters.)
        //
        // "AFK" used to be in this list, and that was wrong on both counts: the client writes
        // `AnimatorManager.AFK` from real AFK detection — headset taken off, or the AFK toggle
        // (`PlayerSetup`, decompiled) — and because AFK is NOT in the client's own core-parameter
        // set, an unprefixed declaration syncs through the ordinary AAS bits, so the wearer's
        // value reaches everyone else exactly as it does in VRChat. An avatar's AFK sign or
        // sleeping pose works in ChilloutVR with no conversion at all, and the report was telling
        // its owner the feature was dead.
        // "VelocityMagnitude" stays in this list for the "#" prefix it confers — the value is
        // computed per-client, so syncing it would be waste — but it is NOT frozen anymore:
        // FeedVelocityMagnitude derives it from the native VelocityX/Y/Z every frame, and the
        // "nothing writes them in game" note below explicitly skips it.
        static readonly HashSet<string> KnownUnsupportedVrcParameters = new HashSet<string>
        {
            "Earmuffs", "AngularY",
            "AvatarVersion", "VelocityMagnitude", "GroundProximity", "InStation",
            "ScaleModified", "ScaleFactor", "ScaleFactorInverse", "EyeHeightAsMeters",
            "EyeHeightAsPercent", "IsAnimatorEnabled"
        };

        public static void Run(BridgeContext ctx)
        {
            var vrcControllers = GetSelectedVrcControllers(ctx);
            bool convertingGestureLayer = vrcControllers.Any(c => c.id == VRCAvatarDescriptor.AnimLayerType.Gesture);

            // Captured before any merging: the saved-controller audit uses these to tell a
            // dead reference INHERITED from the source avatar (already dead in VRChat) from
            // one the conversion introduced (our bug).
            _sourceControllerGuids.Clear();
            foreach (var (_, sourceController) in vrcControllers)
            {
                CollectSerializedGuids(AssetDatabase.GetAssetPath(sourceController), _sourceControllerGuids);
            }

            AnimatorController master = LoadBaseController(ctx, convertingGestureLayer);
            var masterLayers = master.layers.ToList();
            var vrcLayers = new List<AnimatorControllerLayer>();

            // In keep-GoGo mode the Base/Additive/Action layers REPLACE ChilloutVR's locomotion,
            // so they are supposed to run at full weight and drive the body.
            bool gogoDrivesLocomotion = !ctx.Settings.stripGogoLoco && SystemStripper.AvatarUsesGogo(ctx);
            int actionLayersRested = 0;

            foreach (var (id, controller) in vrcControllers)
            {
                // VRChat's ACTION playable layer sits at weight 0 and is raised to 1 by a
                // VRCPlayableLayerControl behaviour only while an emote plays. That is why the
                // stock Action layer can have a Write-Defaults idle state ("WaitForActionOrAFK")
                // holding a full-body clip and harm nothing there: at weight 0 the layer
                // contributes nothing at all.
                //
                // ChilloutVR has no playable layers. Merged into one controller the layer runs at
                // whatever weight it is given, forever — so carrying VRChat's in-controller weight
                // of 1 across hands that idle state the entire body, above locomotion, with no
                // mask. The avatar then stands in its rest pose and NOTHING moves it: movement
                // sliders, walking, crouching, all dead, in the editor and in game alike.
                //
                // Weight 0 is the faithful conversion — it is VRChat's own default. Emotes are
                // unaffected: they come from ChilloutVR's own Locomotion/Emotes layer, which the
                // CCK base controller keeps and the Emote parameter drives.
                bool actionAtRest = id == VRCAvatarDescriptor.AnimLayerType.Action && !gogoDrivesLocomotion;
                var copier = new AnimatorDeepCopier();
                MergeParameters(master, controller, ctx);

                bool firstLayerOfController = true;
                foreach (var srcLayer in controller.layers)
                {
                    if (srcLayer.syncedLayerIndex >= 0)
                    {
                        ctx.Report.Skipped(Category, $"{id} layer \"{srcLayer.name}\"",
                            "Synced layers cannot survive merging into one controller.");
                        continue;
                    }

                    var clone = copier.CloneLayer(srcLayer);
                    // Converted hand-pose layers take over the CCK's LeftHand/RightHand
                    // slots (those were removed above), keeping the controller readable.
                    string cvrHandName = GetCvrHandLayerName(id, srcLayer);
                    clone.name = MakeUniqueLayerName(masterLayers,
                        cvrHandName ?? $"[{id}] {clone.name}");
                    if (cvrHandName != null)
                    {
                        // Silent until a tester spent a round believing these were the CCK's
                        // layers. Saying which layer poses the fingers is what makes an
                        // in-game "fingers don't move" report diagnosable.
                        ctx.Report.Converted(Category,
                            $"Gesture hand layer \"{srcLayer.name}\" -> \"{clone.name}\"",
                            "Takes over ChilloutVR's hand-pose slot: the CCK's own layer was " +
                            "dropped and this one — the avatar's actual finger animations — " +
                            "drives the fingers. Its VRChat hand mask is replaced with an " +
                            "equivalent generated copy (same humanoid bits, verified against " +
                            "VRChat's own vrc_Hand masks).");
                    }
                    if (firstLayerOfController)
                    {
                        // Unity forces a controller's first layer to weight 1; once merged it
                        // is no longer first, so bake that weight in.
                        clone.defaultWeight = 1f;
                        firstLayerOfController = false;
                    }
                    if (actionAtRest)
                    {
                        // After the first-layer rule above, which would otherwise re-raise it.
                        clone.defaultWeight = 0f;
                        actionLayersRested++;
                    }
                    clone.avatarMask = ReplaceVrcMask(clone.avatarMask, ctx);
                    masterLayers.Add(clone);
                    vrcLayers.Add(clone);
                }
                ctx.Report.Converted(Category, $"{id} layer merged", $"{controller.layers.Length} sub-layers");
            }

            if (actionLayersRested > 0)
            {
                ctx.Report.Converted(Category,
                    $"Action layer merged at weight 0, the weight VRChat gives it",
                    "VRChat keeps the Action playable layer at weight 0 and raises it only while an " +
                    "emote plays, which is why its idle state can hold a full-body clip with Write " +
                    "Defaults on and harm nothing. ChilloutVR has no playable layers, so carrying " +
                    "weight 1 across would let that idle state hold your whole body in its rest pose " +
                    "above locomotion — walking, crouching and the movement sliders would all do " +
                    "nothing. Emotes are unaffected: they play from ChilloutVR's own " +
                    "Locomotion/Emotes layer, driven by the Emote parameter. If you deliberately want " +
                    "this layer live, raise its weight in the Animator window.");
            }

            master.layers = masterLayers.ToArray();

            _gestureConditionsRedirected = 0;
            GesturePass(master, vrcLayers, ctx);
            // The CCK's kept hand layers are deliberately NOT touched: they already condition
            // on the gesture floats, which is the stock idiom the client actually runs.
            if (_gestureConditionsRedirected > 0)
            {
                ctx.Report.Converted(Category,
                    $"{_gestureConditionsRedirected} gesture condition(s) rebuilt as ChilloutVR float threshold bands",
                    "Discrete VRChat gesture checks (GestureLeft == 4) become the CCK's own float windows " +
                    "(GestureLeft > 3.9 and < 4.1) — the exact idiom the stock avatar animator uses, so the " +
                    "conversion rides the same client path as every avatar that ships with the game. The " +
                    "fist band starts at 0.1: the float carries the analog grip there, and a light squeeze " +
                    "counts as fist, like VRChat. EDITOR TESTING: drive the GestureLeft/GestureRight FLOAT " +
                    "(-1 open, 0.1..1 fist, 2 thumbs up, 3 gun, 4 point, 5 peace, 6 rock'n'roll) — the CCK " +
                    "Animator Tester's pose buttons do exactly that. IN GAME on Index-type controllers, " +
                    "gestures only register while \"Skeletal Input\" or \"Infer Gestures from Finger " +
                    "Tracking\" is enabled in ChilloutVR's settings — with both off, NO avatar gestures, " +
                    "stock or converted.");
            }
            RebuildAnalogFist(master, ctx);
            BehaviourPass(master, vrcLayers, ctx);
            SystemStripper.Run(ctx, master, vrcLayers);
            StripExistingFaceTracking(master, vrcLayers, ctx);
            ToggleNativizer.Run(ctx, master, vrcLayers);
            // Before RenamePass, so the menu entries' machineNames still line up with the
            // animator parameter names; and before CompactIntDropdowns, which needs the
            // dropdown parameters to already be Ints.
            ParameterTypeInference.Run(master, ctx);
            RenamePass(master, vrcLayers, ctx);
            ApplyParameterDefaults(master, ctx);
            ReconcileAasInputTypes(master, ctx);
            CreateParameterStreams(master, ctx);
            FeedVelocityMagnitude(master, ctx);
            RehomeVolatileAssets(master, vrcLayers, ctx);
            DeduplicateLayers(master, ctx);
            MaskMergedLayers(master, vrcLayers, ctx);
            WarnLocomotionOverrides(vrcLayers, ctx);
            FaceTrackingInjector.Inject(master, ctx);
            AvatarScalerInjector.Inject(master, ctx);
            // After every layer that will exist, exists: the stack order it checks is the one
            // the game will run.
            AuditHandPoseConflicts(master, convertingGestureLayer, ctx);
            // Run last: after every merge and injection, make sure no transition conditions
            // it on a parameter using a comparison its final type can't express (e.g. a
            // Float/Bool type-conflict that keeps Float but leaves bool-style If/IfNot
            // conditions behind). ChilloutVR silently drops such transitions.
            ReconcileConditionModes(master, ctx);
            SyncDriverParameterTypes(master, ctx);
            RepairUnconditionalDriverStates(master, ctx);
            VerifyMenuParameterNames(master, ctx);
            PruneDeadMenuEntries(master, ctx);
            CompactIntDropdowns(master, ctx);
            // After the menu is final. SystemStripper already drops unreferenced parameters, but
            // it runs long before this and keeps anything a menu entry drives — so a parameter
            // whose only justification was an entry that PruneDeadMenuEntries then removed is
            // left behind, declared and inert, still costing sync bits if it was synced.
            // Before pruning, while every reference is still visible.
            RepairPrefixedReferences(master, ctx);
            DeclareDanglingParameters(master, ctx);
            DefaultUnsupportedBuiltIns(master, ctx);
            PruneOrphanedParameters(master, ctx);

            // After every merge, injection and clip clone, right before saving: animations that
            // toggled a converted PhysBone's GameObject or component are taught to reach the
            // generated physics as well.
            RewirePhysicsToggles(master, ctx);
            RepairClipPaths(master, ctx);
            AuditClipBindings(master, ctx);
            AuditCurveControlledGameParameters(master, ctx);

            master.name = SanitizeFileName(ctx.Target.name) + "_CVR";

            // Persist controller + override controller and hook both to the CVRAvatar.
            // Save hands back the persisted asset, which is a different object whenever an
            // earlier run's controller was overwritten in place to keep its GUID — so
            // everything below must reference that one, not the object we built.
            string controllerPath = $"{ctx.OutputDir}/{master.name}.controller";
            // Counted before and after saving, because Unity's persistence layer can silently
            // amputate: an object flagged DontSave is refused at save time (an assertion in the
            // editor log nobody reads) and its reference goes dangling — "Missing (Motion)" in
            // the animator window, dead toggles in game, and a conversion report with no errors.
            // If anything the controller referenced in memory failed to arrive on disk, that is
            // an Error, in the report, with numbers.
            int motionsBeforeSave = CountMotionReferences(master);
            master = AnimatorAssetSaver.Save(master, controllerPath);
            // The serialized-guid audit runs from BridgeConverter AFTER AnimationSelfContainer,
            // so it judges the FINAL file — auditing here flagged references the self-container
            // was about to repoint, and told a user "do not upload" a fine conversion.
            int motionsAfterSave = CountMotionReferences(master);
            if (motionsAfterSave < motionsBeforeSave)
            {
                ctx.Report.Error(Category,
                    $"{motionsBeforeSave - motionsAfterSave} animation reference(s) failed to persist",
                    $"The controller referenced {motionsBeforeSave} motions in memory but only " +
                    $"{motionsAfterSave} survived saving to disk. The usual cause is a VRCFury bake " +
                    "that errored partway, leaving generated clips unsaveable — check this report for " +
                    "a VRCFury error above, fix the source avatar's Fury setup, and convert again. " +
                    "Do not upload this conversion: every missing motion is a dead toggle or animation.");
            }
            ctx.MergedController = master;

            // Generate the override controller wrapping the base. ChilloutVR uses this as
            // the avatar's runtime controller (it's what the "Override Controller" slot
            // expects), so it must be present, not left empty.
            var overrides = new AnimatorOverrideController(master) { name = master.name + "_Overrides" };
            string overridesPath = $"{ctx.OutputDir}/{overrides.name}.overrideController";
            overrides = AnimatorAssetSaver.SaveOverride(overrides, overridesPath);

            ctx.CvrAvatar.avatarSettings.baseController = master;
            // The CCK's own "Override Controller" slot. Its Advanced Settings editor reads this
            // when it regenerates a controller, so leaving it empty quietly loses the overrides
            // the moment anyone uses the CCK's own controller-creation button.
            ctx.CvrAvatar.avatarSettings.baseOverrideController = overrides;
            ctx.CvrAvatar.overrides = overrides;

            var animator = ctx.TargetAnimator;
            if (animator != null)
            {
                // The override, not the base. ChilloutVR does this itself on load — AssetFilter
                // assigns CVRAvatar.overrides onto the Animator — so pointing at the base here
                // left the editor showing something the game never runs, and play-mode preview
                // disagreeing with the real thing.
                animator.runtimeAnimatorController = overrides;
            }
            EditorUtility.SetDirty(ctx.CvrAvatar);
        }

        // ------------------------------------------------------------------ setup ----

        static List<(VRCAvatarDescriptor.AnimLayerType id, AnimatorController controller)> GetSelectedVrcControllers(BridgeContext ctx)
        {
            var result = new List<(VRCAvatarDescriptor.AnimLayerType, AnimatorController)>();
            foreach (var layer in ctx.SourceDescriptor.baseAnimationLayers)
            {
                bool wanted;
                switch (layer.type)
                {
                    case VRCAvatarDescriptor.AnimLayerType.Base: wanted = ctx.Settings.convertBaseLayer; break;
                    case VRCAvatarDescriptor.AnimLayerType.Additive: wanted = ctx.Settings.convertAdditiveLayer; break;
                    case VRCAvatarDescriptor.AnimLayerType.Gesture: wanted = ctx.Settings.convertGestureLayer; break;
                    case VRCAvatarDescriptor.AnimLayerType.Action: wanted = ctx.Settings.convertActionLayer; break;
                    case VRCAvatarDescriptor.AnimLayerType.FX: wanted = ctx.Settings.convertFxLayer; break;
                    default: wanted = false; break;
                }
                if (!wanted || layer.isDefault)
                {
                    continue;
                }
                if (layer.animatorController is AnimatorController controller)
                {
                    result.Add((layer.type, controller));
                }
            }
            return result;
        }

        static AnimatorController LoadBaseController(BridgeContext ctx, bool convertingGestureLayer)
        {
            AnimatorController source = null;
            foreach (var path in CckAnimatorPaths)
            {
                source = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (source != null)
                {
                    break;
                }
            }

            var master = new AnimatorController();

            if (source == null)
            {
                ctx.Report.Warning(Category, "CCK AvatarAnimator.controller not found",
                    "Locomotion/hand layers are missing; the CCK usually regenerates them, but check the result.");
                // Names, types and order taken from the CCK's own AvatarAnimator.controller.
                // ChilloutVR's animator manager dispatches writes on the *declared* type, so
                // these have to match the real thing rather than be approximated: this list
                // previously had Grounded as a Float, Emote and Toggle as Ints, and omitted the
                // five locomotion Bools entirely, which left anything conditioning on Sitting or
                // Flying referencing a parameter nothing declared.
                master.parameters = new[]
                {
                    new AnimatorControllerParameter { name = "MovementX", type = AnimatorControllerParameterType.Float },
                    new AnimatorControllerParameter { name = "MovementY", type = AnimatorControllerParameterType.Float },
                    new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true },
                    new AnimatorControllerParameter { name = "Emote", type = AnimatorControllerParameterType.Float },
                    new AnimatorControllerParameter { name = "CancelEmote", type = AnimatorControllerParameterType.Trigger },
                    new AnimatorControllerParameter { name = "GestureLeft", type = AnimatorControllerParameterType.Float },
                    new AnimatorControllerParameter { name = "GestureRight", type = AnimatorControllerParameterType.Float },
                    new AnimatorControllerParameter { name = "Toggle", type = AnimatorControllerParameterType.Float },
                    new AnimatorControllerParameter { name = "Sitting", type = AnimatorControllerParameterType.Bool },
                    new AnimatorControllerParameter { name = "Crouching", type = AnimatorControllerParameterType.Bool },
                    new AnimatorControllerParameter { name = "Prone", type = AnimatorControllerParameterType.Bool },
                    new AnimatorControllerParameter { name = "Flying", type = AnimatorControllerParameterType.Bool },
                    new AnimatorControllerParameter { name = "Swimming", type = AnimatorControllerParameterType.Bool }
                };
                return master;
            }

            // When the VRC Gesture layer takes over hand animation, CVR's own hand layers
            // must go or they fight for the finger muscles.
            var allowed = new List<string> { "Locomotion/Emotes" };
            if (!convertingGestureLayer)
            {
                allowed.Add("LeftHand");
                allowed.Add("RightHand");
            }

            // Keeping GoGo Loco (strip off) means GoGo IS the locomotion: its Base/Poses/Action
            // layers replace ChilloutVR's own the same way they replace VRChat's. Leaving the
            // CCK's Locomotion/Emotes underneath had the two fighting for the body every frame
            // — the tester-visible result was CVR animations with GoGo flickering over them.
            // Known, accepted losses in this mode (no CVR equivalents exist): movement is not
            // locked during poses (walking mid-pose slides), the viewpoint does not follow
            // pose height, and CVR's own quick-menu emotes no longer animate — GoGo's wheel
            // replaces them.
            if (!ctx.Settings.stripGogoLoco && SystemStripper.AvatarUsesGogo(ctx))
            {
                allowed.Remove("Locomotion/Emotes");
                ctx.Report.Warning(Category,
                    "GoGo Loco kept: ChilloutVR's own Locomotion/Emotes layer removed",
                    "GoGo's Base/Poses/Action layers replace it, driven by the game-fed velocity " +
                    "and upright parameters. EXPERIMENTAL, with known limits ChilloutVR cannot " +
                    "express: poses don't lock movement (walking mid-pose slides), the viewpoint " +
                    "stays at standing height in floor poses, and CVR's quick-menu emotes won't " +
                    "animate — use GoGo's own wheel. Merge the Base, Additive and Action layers " +
                    "or this avatar has NO locomotion at all.");
            }
            string[] allowedLayers = allowed.ToArray();

            var copier = new AnimatorDeepCopier();
            master.parameters = source.parameters.Select(AnimatorDeepCopier.CloneParameter).ToArray();
            master.layers = source.layers
                .Where(l => allowedLayers.Contains(l.name))
                .Select(copier.CloneLayer)
                .ToArray();

            ctx.Report.Converted(Category, "CCK base animator",
                master.layers.Length > 0
                    ? $"Kept layers: {string.Join(", ", master.layers.Select(l => l.name))}"
                    : "Kept layers: none — GoGo replaces ChilloutVR's locomotion, and the " +
                      "avatar's own gesture layers replace the CCK's hand-pose layers.");
            return master;
        }

        static void MergeParameters(AnimatorController master, AnimatorController source, BridgeContext ctx)
        {
            var masterParams = master.parameters.ToList();
            foreach (var srcParam in source.parameters)
            {
                var param = AnimatorDeepCopier.CloneParameter(srcParam);
                if (GestureMap.GestureParameters.Contains(param.name))
                {
                    param.type = AnimatorControllerParameterType.Float; // CVR gestures are floats
                }

                var existing = masterParams.FirstOrDefault(p => p.name == param.name);
                if (existing == null)
                {
                    masterParams.Add(param);
                }
                else if (existing.type != param.type &&
                         !GestureMap.GestureParameters.Contains(param.name))
                {
                    ctx.Report.Warning(Category, $"Parameter \"{param.name}\"",
                        $"Type conflict between controllers ({existing.type} vs {param.type}); keeping {existing.type}.");
                }
            }
            master.parameters = masterParams.ToArray();
        }

        // --------------------------------------------------------------- gestures ----

        static void GesturePass(AnimatorController master, List<AnimatorControllerLayer> vrcLayers, BridgeContext ctx)
        {
            foreach (var layer in vrcLayers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        RemapMotionGestureParameters(child.state.motion, ctx);
                        child.state.transitions = RewriteTransitions(child.state.transitions, ctx);
                    }
                    machine.anyStateTransitions = RewriteTransitions(machine.anyStateTransitions, ctx);
                    machine.entryTransitions = RewriteTransitions(machine.entryTransitions, ctx);
                    foreach (var childMachine in machine.stateMachines)
                    {
                        var transitions = machine.GetStateMachineTransitions(childMachine.stateMachine);
                        if (transitions != null && transitions.Length > 0)
                        {
                            machine.SetStateMachineTransitions(childMachine.stateMachine,
                                RewriteTransitions(transitions, ctx));
                        }
                    }
                });
            }
        }

        static void RemapMotionGestureParameters(Motion motion, BridgeContext ctx)
        {
            if (!(motion is BlendTree tree))
            {
                return;
            }
            foreach (var child in tree.children)
            {
                RemapMotionGestureParameters(child.motion, ctx);
            }

            bool remapX = GestureMap.GestureParameters.Contains(tree.blendParameter);
            bool remapY = (tree.blendType == BlendTreeType.SimpleDirectional2D ||
                           tree.blendType == BlendTreeType.FreeformDirectional2D ||
                           tree.blendType == BlendTreeType.FreeformCartesian2D) &&
                          GestureMap.GestureParameters.Contains(tree.blendParameterY);

            if (tree.blendType == BlendTreeType.Simple1D && remapX)
            {
                // Thresholds hold VRC gesture ints; convert and re-sort for CVR values.
                var children = tree.children;
                for (int i = 0; i < children.Length; i++)
                {
                    children[i].threshold = GestureMap.VrcToCvr(children[i].threshold);
                }
                Array.Sort(children, (a, b) => a.threshold.CompareTo(b.threshold));
                tree.useAutomaticThresholds = false;
                tree.children = children;
                ctx.Report.Converted(Category, $"Blend tree \"{tree.name}\"", "Gesture thresholds remapped to CVR values.");
            }
            else if (remapX || remapY)
            {
                ctx.Report.Warning(Category, $"Blend tree \"{tree.name}\"",
                    "2D blend tree driven by a gesture parameter; CVR gesture values differ, check manually.");
            }
        }

        static T[] RewriteTransitions<T>(T[] transitions, BridgeContext ctx) where T : AnimatorTransitionBase, new()
        {
            var result = new List<T>();
            foreach (var transition in transitions)
            {
                var branches = RewriteConditions(transition.conditions, ctx);
                if (branches == null) // no gesture conditions involved
                {
                    result.Add(transition);
                    continue;
                }
                foreach (var branch in branches)
                {
                    var clone = CloneForBranch(transition);
                    clone.conditions = branch;
                    result.Add(clone);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Returns null when no condition needed rewriting; otherwise the OR-branches
        /// (each an AND-group) replacing the original condition list.
        /// </summary>
        static List<AnimatorCondition[]> RewriteConditions(AnimatorCondition[] conditions, BridgeContext ctx)
        {
            bool anyChanged = false;
            var branchSets = new List<List<AnimatorCondition>> { new List<AnimatorCondition>() };

            foreach (var condition in conditions)
            {
                List<List<AnimatorCondition>> options;

                if (GestureMap.GestureParameters.Contains(condition.parameter))
                {
                    options = RewriteGestureCondition(condition, ctx);
                    anyChanged = true;
                }
                else
                {
                    // GestureLeftWeight/RightWeight conditions pass through unchanged: a
                    // CVRParameterStream feeds those parameters the real trigger values.
                    options = new List<List<AnimatorCondition>> { new List<AnimatorCondition> { condition } };
                }

                // Cartesian product of existing branches with this condition's options.
                var next = new List<List<AnimatorCondition>>();
                foreach (var branch in branchSets)
                {
                    foreach (var option in options)
                    {
                        var combined = new List<AnimatorCondition>(branch);
                        combined.AddRange(option);
                        next.Add(combined);
                    }
                }
                branchSets = next;

                if (branchSets.Count > MaxConditionBranches)
                {
                    ctx.Report.Warning(Category, "Transition condition explosion",
                        $"More than {MaxConditionBranches} branches while rewriting gesture conditions; truncating.");
                    branchSets = branchSets.Take(MaxConditionBranches).ToList();
                }
            }

            if (!anyChanged)
            {
                return null;
            }
            return branchSets.Select(b => b.ToArray()).ToList();
        }

        static List<List<AnimatorCondition>> RewriteGestureCondition(AnimatorCondition condition, BridgeContext ctx)
        {
            // Rebuild discrete gesture checks as threshold bands on the GestureLeft/GestureRight
            // FLOATS — the exact idiom the CCK's own AvatarAnimator uses, and the only one the
            // client is demonstrably exercised against. The integer GestureLeftIdx route looked
            // equivalent in the decompile (Idx = round(GestureLeft) in the parameter setter),
            // but a tester's split verdict — stock avatar poses fingers, converted avatar with
            // identical clips, masks and wiring doesn't — isolated the difference to exactly
            // this: the stock controller never references Idx. Condition like the CCK does.
            string param = condition.parameter;
            _gestureConditionsRedirected++;

            if (condition.mode == AnimatorConditionMode.Equals)
            {
                return new List<List<AnimatorCondition>>
                {
                    FloatBand(param, GestureMap.VrcToCvrIdx((int)condition.threshold))
                };
            }
            if (condition.mode == AnimatorConditionMode.NotEqual)
            {
                return FloatBandInverse(param, GestureMap.VrcToCvrIdx((int)condition.threshold));
            }

            // Greater/Less compare VRChat's numeric ordering, which differs from CVR's, so
            // enumerate the matching gestures and OR their bands.
            var matched = new List<int>();
            for (int g = 0; g <= 7; g++)
            {
                bool match = condition.mode == AnimatorConditionMode.Greater ? g > condition.threshold
                           : condition.mode == AnimatorConditionMode.Less ? g < condition.threshold
                           : true;
                if (match)
                {
                    matched.Add(g);
                }
            }

            if (matched.Count == 8)
            {
                // Always true; drop the condition entirely.
                return new List<List<AnimatorCondition>> { new List<AnimatorCondition>() };
            }
            if (matched.Count == 0)
            {
                // Never true; a value the gesture float can't reach ([-1..6]).
                return new List<List<AnimatorCondition>>
                {
                    new List<AnimatorCondition>
                    {
                        new AnimatorCondition
                        {
                            parameter = param,
                            mode = AnimatorConditionMode.Greater,
                            threshold = 98f
                        }
                    }
                };
            }

            return matched
                .Select(g => FloatBand(param, GestureMap.VrcToCvrIdx(g)))
                .ToList();
        }

        /// <summary>
        /// Analog fist parity for taken-over hand layers. In VRChat a fist isn't a snap: the
        /// gesture playable blends the fist pose in by grip strength. The CCK does the same
        /// with its Relaxed/Fist blend tree on the gesture float — the float IS the grip in
        /// the fist band. A converted hand layer whose Fist state plays a bare clip would
        /// snap to full fist at a light squeeze instead, so the clip is wrapped in the CCK's
        /// own idiom: a 1D tree on the gesture float, idle pose at 0.1, fist pose at 1.
        /// Skipped when the fist state already uses a tree or motion-time weight — that
        /// author built their own analog handling and it converts as-is.
        /// </summary>
        static void RebuildAnalogFist(AnimatorController master, BridgeContext ctx)
        {
            foreach (var layer in master.layers)
            {
                string param = layer.name == "LeftHand" ? "GestureLeft"
                    : layer.name == "RightHand" ? "GestureRight" : null;
                if (param == null || layer.stateMachine == null)
                {
                    continue;
                }
                AnimatorState fist = null, idle = null;
                void Classify(AnimatorCondition[] conditions, AnimatorState dst)
                {
                    if (dst == null)
                    {
                        return;
                    }
                    bool fistLo = false, fistHi = false, idleLo = false, idleHi = false;
                    foreach (var c in conditions)
                    {
                        if (c.parameter != param)
                        {
                            continue;
                        }
                        if (c.mode == AnimatorConditionMode.Greater && Mathf.Abs(c.threshold - 0.1f) < 0.01f) fistLo = true;
                        if (c.mode == AnimatorConditionMode.Less && Mathf.Abs(c.threshold - 1.1f) < 0.01f) fistHi = true;
                        if (c.mode == AnimatorConditionMode.Greater && Mathf.Abs(c.threshold + 0.9f) < 0.01f) idleLo = true;
                        if (c.mode == AnimatorConditionMode.Less && Mathf.Abs(c.threshold - 0.1f) < 0.01f) idleHi = true;
                    }
                    if (fistLo && fistHi && fist == null)
                    {
                        fist = dst;
                    }
                    if (idleLo && idleHi && idle == null)
                    {
                        idle = dst;
                    }
                }
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var t in machine.anyStateTransitions)
                    {
                        Classify(t.conditions, t.destinationState);
                    }
                    foreach (var child in machine.states)
                    {
                        foreach (var t in child.state.transitions)
                        {
                            Classify(t.conditions, t.destinationState);
                        }
                    }
                });
                if (fist == null || idle == null || fist == idle || fist.timeParameterActive)
                {
                    continue;
                }
                if (!(fist.motion is AnimationClip fistClip) || !(idle.motion is AnimationClip idleClip))
                {
                    continue;
                }
                var tree = new BlendTree
                {
                    name = "AnalogFist",
                    blendType = BlendTreeType.Simple1D,
                    blendParameter = param,
                    useAutomaticThresholds = false,
                    hideFlags = HideFlags.HideInHierarchy,
                };
                tree.children = new[]
                {
                    new ChildMotion { motion = idleClip, threshold = 0.1f, timeScale = 1f },
                    new ChildMotion { motion = fistClip, threshold = 1f, timeScale = 1f },
                };
                fist.motion = tree;
                ctx.Report.Converted(Category, $"{layer.name}: analog fist curl rebuilt",
                    $"\"{fist.name}\" now blends from \"{idleClip.name}\" (grip 0.1) to \"{fistClip.name}\" " +
                    "(full grip) on the gesture float — the CCK's own Relaxed/Fist idiom, matching how " +
                    "VRChat eases the fist in by trigger pressure instead of snapping to the full pose.");
            }
        }

        /// <summary>
        /// The CCK's own detection window for one gesture value, as serialized in its
        /// AvatarAnimator: open (-1) is "&lt; -0.9", rock'n'roll (6) is "&gt; 5.9"
        /// (open-ended), the discrete poses sit in (V-0.1, V+0.1). The CCK folds neutral and
        /// fist into one (-0.9, 1.1) band because the float IS the analog grip there; VRChat
        /// logic has separate neutral/fist states, so those split at 0.1 — grip past a light
        /// squeeze counts as fist, mirroring VRChat's own low trigger threshold.
        /// </summary>
        static List<AnimatorCondition> FloatBand(string param, int cvrValue)
        {
            var band = new List<AnimatorCondition>();
            void Add(AnimatorConditionMode mode, float threshold) => band.Add(
                new AnimatorCondition { parameter = param, mode = mode, threshold = threshold });
            switch (cvrValue)
            {
                case -1: Add(AnimatorConditionMode.Less, -0.9f); break;
                case 0: Add(AnimatorConditionMode.Greater, -0.9f); Add(AnimatorConditionMode.Less, 0.1f); break;
                case 1: Add(AnimatorConditionMode.Greater, 0.1f); Add(AnimatorConditionMode.Less, 1.1f); break;
                case 6: Add(AnimatorConditionMode.Greater, 5.9f); break;
                default:
                    Add(AnimatorConditionMode.Greater, cvrValue - 0.1f);
                    Add(AnimatorConditionMode.Less, cvrValue + 0.1f);
                    break;
            }
            return band;
        }

        /// <summary>
        /// Everything OUTSIDE one gesture's band, as OR-branches — Unity evaluates only
        /// Greater/Less on float parameters, so a NotEqual has to become two transitions.
        /// </summary>
        static List<List<AnimatorCondition>> FloatBandInverse(string param, int cvrValue)
        {
            List<AnimatorCondition> One(AnimatorConditionMode mode, float threshold) =>
                new List<AnimatorCondition>
                {
                    new AnimatorCondition { parameter = param, mode = mode, threshold = threshold }
                };
            switch (cvrValue)
            {
                case -1:
                    return new List<List<AnimatorCondition>> { One(AnimatorConditionMode.Greater, -0.9f) };
                case 0:
                    return new List<List<AnimatorCondition>>
                        { One(AnimatorConditionMode.Less, -0.9f), One(AnimatorConditionMode.Greater, 0.1f) };
                case 1:
                    return new List<List<AnimatorCondition>>
                        { One(AnimatorConditionMode.Less, 0.1f), One(AnimatorConditionMode.Greater, 1.1f) };
                case 6:
                    return new List<List<AnimatorCondition>> { One(AnimatorConditionMode.Less, 5.9f) };
                default:
                    return new List<List<AnimatorCondition>>
                    {
                        One(AnimatorConditionMode.Less, cvrValue - 0.1f),
                        One(AnimatorConditionMode.Greater, cvrValue + 0.1f)
                    };
            }
        }

        static T CloneForBranch<T>(T src) where T : AnimatorTransitionBase, new()
        {
            var dst = new T
            {
                name = src.name,
                destinationState = src.destinationState,
                destinationStateMachine = src.destinationStateMachine,
                isExit = src.isExit,
                solo = src.solo,
                mute = src.mute,
                hideFlags = HideFlags.HideInHierarchy
            };
            if (src is AnimatorStateTransition s && dst is AnimatorStateTransition d)
            {
                d.duration = s.duration;
                d.offset = s.offset;
                d.exitTime = s.exitTime;
                d.hasExitTime = s.hasExitTime;
                d.hasFixedDuration = s.hasFixedDuration;
                d.interruptionSource = s.interruptionSource;
                d.orderedInterruption = s.orderedInterruption;
                d.canTransitionToSelf = s.canTransitionToSelf;
            }
            return dst;
        }

        // ------------------------------------------------------------- behaviours ----

        static void BehaviourPass(AnimatorController master, List<AnimatorControllerLayer> vrcLayers, BridgeContext ctx)
        {
            var skippedBehaviourCounts = new Dictionary<string, int>();
            var bodyControlStats = new BodyControlStats();

            foreach (var layer in vrcLayers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    machine.behaviours = ConvertBehaviours(master, machine.behaviours, null, ctx, skippedBehaviourCounts, bodyControlStats);
                    foreach (var child in machine.states)
                    {
                        child.state.behaviours = ConvertBehaviours(master, child.state.behaviours, child.state, ctx, skippedBehaviourCounts, bodyControlStats);
                    }
                });
            }

            bodyControlStats.Report(ctx);

            foreach (var pair in skippedBehaviourCounts)
            {
                ctx.Report.Skipped(Category, pair.Key, $"{pair.Value}x removed (no ChilloutVR equivalent).");
            }
        }

        static StateMachineBehaviour[] ConvertBehaviours(AnimatorController master, StateMachineBehaviour[] behaviours,
            AnimatorState state, BridgeContext ctx, Dictionary<string, int> skipped, BodyControlStats bodyStats)
        {
            if (behaviours == null || behaviours.Length == 0)
            {
                return behaviours;
            }

            var result = new List<StateMachineBehaviour>();
            // One BodyControl per state, shared by every tracking/locomotion behaviour on it.
            // Two components on one state would both run in an undefined order, and the CCK's
            // own OnValidate would not be able to reconcile them.
            BodyControl bodyControl = null;

            foreach (var behaviour in behaviours)
            {
                if (behaviour == null)
                {
                    continue;
                }
                if (behaviour is VRCAvatarParameterDriver vrcDriver)
                {
                    var driver = ConvertParameterDriver(master, vrcDriver, ctx);
                    if (driver != null)
                    {
                        result.Add(driver);
                    }
                    UnityEngine.Object.DestroyImmediate(behaviour, true);
                }
                else if (behaviour is VRCAnimatorTrackingControl tracking)
                {
                    ConvertTrackingControl(tracking, ref bodyControl, result, bodyStats);
                    UnityEngine.Object.DestroyImmediate(behaviour, true);
                }
                else if (behaviour is VRCAnimatorLocomotionControl locomotion)
                {
                    AddBodyTask(ref bodyControl, result, BodyControlTask.BodyMask.Locomotion,
                        locomotion.disableLocomotion ? 0f : 1f, bodyStats);
                    UnityEngine.Object.DestroyImmediate(behaviour, true);
                }
                else if (behaviour.GetType().Name.StartsWith("VRC"))
                {
                    skipped[behaviour.GetType().Name] = skipped.TryGetValue(behaviour.GetType().Name, out var n) ? n + 1 : 1;
                    UnityEngine.Object.DestroyImmediate(behaviour, true);
                }
                else
                {
                    result.Add(behaviour);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Running totals for the tracking/locomotion conversion, so the report gets one line
        /// instead of one per behaviour. Avatars carry dozens of these.
        /// </summary>
        class BodyControlStats
        {
            public int Components;
            public int Tasks;
            public int DroppedEyes;
            public int DroppedMouth;
            public int DroppedFingers;

            public void Report(BridgeContext ctx)
            {
                if (Components == 0)
                {
                    return;
                }
                ctx.Report.Converted(Category,
                    $"{Components} tracking/locomotion behaviour(s) converted to ChilloutVR Body Control",
                    $"{Tasks} body-mask task(s) written. \"Animation\" becomes weight 0, which switches that " +
                    "limb's FinalIK solver off so the animation drives it; \"Tracking\" becomes weight 1, which " +
                    "hands it back to IK. Dropping these left every limb at weight 1, so IK overrode the " +
                    "avatar's own animation — the usual symptom is a body locked in its rest pose while the " +
                    "head still tracks.");

                int unmapped = DroppedEyes + DroppedMouth + DroppedFingers;
                if (unmapped > 0)
                {
                    var parts = new List<string>();
                    if (DroppedEyes > 0) parts.Add($"eyes/eyelids ({DroppedEyes})");
                    if (DroppedMouth > 0) parts.Add($"mouth/jaw ({DroppedMouth})");
                    if (DroppedFingers > 0) parts.Add($"fingers ({DroppedFingers})");
                    ctx.Report.Approximated(Category, "Some tracking targets have no ChilloutVR body mask",
                        $"{string.Join(", ", parts)} — ChilloutVR's Body Control covers head, pelvis, arms, legs " +
                        "and locomotion only. These targets keep whatever the avatar's own animation and face " +
                        "tracking do with them.");
                }
            }
        }

        /// <summary>
        /// VRChat's per-limb tracking toggle, expressed as ChilloutVR Body Control tasks.
        ///
        /// The two systems line up exactly, which is worth stating because it isn't documented —
        /// ChilloutVR's own docs page for Body Control is an empty placeholder. In the client,
        /// BodyControlTask.Execute writes BodySystem.BodyControl{Head,Pelvis,LeftArm,…}, and
        /// IKHandler.UpdateWeights feeds those straight into the FinalIK VRIK solver as
        /// positionWeight/rotationWeight. Weight 0 means the solver stops driving that limb and
        /// the animation wins; weight 1 means IK overrides the animation. That is precisely
        /// VRChat's TrackingType.Animation and TrackingType.Tracking.
        ///
        /// Eyes, mouth and fingers have no equivalent mask — the CCK carries a
        /// "TODO: Add FingerTracking masks when GS is ready" — so they are counted and reported.
        /// </summary>
        static void ConvertTrackingControl(VRCAnimatorTrackingControl tracking, ref BodyControl bodyControl,
            List<StateMachineBehaviour> result, BodyControlStats stats)
        {
            // Written out rather than looped: a local function may not capture a ref parameter.
            MapTracking(tracking.trackingHead, BodyControlTask.BodyMask.Head, ref bodyControl, result, stats);
            MapTracking(tracking.trackingHip, BodyControlTask.BodyMask.Pelvis, ref bodyControl, result, stats);
            MapTracking(tracking.trackingLeftHand, BodyControlTask.BodyMask.LeftArm, ref bodyControl, result, stats);
            MapTracking(tracking.trackingRightHand, BodyControlTask.BodyMask.RightArm, ref bodyControl, result, stats);
            MapTracking(tracking.trackingLeftFoot, BodyControlTask.BodyMask.LeftLeg, ref bodyControl, result, stats);
            MapTracking(tracking.trackingRightFoot, BodyControlTask.BodyMask.RightLeg, ref bodyControl, result, stats);

            if (Changes(tracking.trackingEyes)) stats.DroppedEyes++;
            if (Changes(tracking.trackingMouth)) stats.DroppedMouth++;
            if (Changes(tracking.trackingLeftFingers) || Changes(tracking.trackingRightFingers))
            {
                stats.DroppedFingers++;
            }
        }

        static bool Changes(VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType type) =>
            type != VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.NoChange;

        static void MapTracking(VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType type,
            BodyControlTask.BodyMask mask, ref BodyControl bodyControl,
            List<StateMachineBehaviour> result, BodyControlStats stats)
        {
            if (!Changes(type))
            {
                return;
            }
            AddBodyTask(ref bodyControl, result, mask,
                type == VRC.SDKBase.VRC_AnimatorTrackingControl.TrackingType.Tracking ? 1f : 0f, stats);
        }

        /// <summary>
        /// Adds one body-mask task, creating the state's BodyControl on first use.
        ///
        /// Last write wins, matching both VRChat (behaviours run in list order, so a later one
        /// overwrites an earlier one) and the CCK, whose OnValidate walks the list backwards
        /// dropping earlier duplicates. Doing it here means the asset already looks the way the
        /// CCK would rewrite it, so merely opening the inspector can't change behaviour.
        ///
        /// isBlend is left false deliberately: the client's ExecuteOverTime is an empty method,
        /// so a blend task applies nothing at all.
        /// </summary>
        static void AddBodyTask(ref BodyControl bodyControl, List<StateMachineBehaviour> result,
            BodyControlTask.BodyMask mask, float weight, BodyControlStats stats)
        {
            if (bodyControl == null)
            {
                bodyControl = ScriptableObject.CreateInstance<BodyControl>();
                bodyControl.name = "BodyControl";
                result.Add(bodyControl);
                stats.Components++;
            }
            bodyControl.EnterTasks.RemoveAll(t => t.target == mask);
            bodyControl.EnterTasks.Add(new BodyControlTask
            {
                target = mask,
                targetWeight = weight,
                transitionDuration = 0f,
                isBlend = false,
            });
            stats.Tasks++;
        }

        static AnimatorDriver ConvertParameterDriver(AnimatorController master, VRCAvatarParameterDriver vrcDriver, BridgeContext ctx)
        {
            var driver = ScriptableObject.CreateInstance<AnimatorDriver>();
            driver.name = "AnimatorDriver";
            driver.hideFlags = HideFlags.HideInHierarchy;
            driver.localOnly = vrcDriver.localOnly;

            AnimatorDriverTask.ParameterType TypeOf(string parameterName)
            {
                var param = master.parameters.FirstOrDefault(p => p.name == parameterName);
                if (param == null)
                {
                    return AnimatorDriverTask.ParameterType.Float;
                }
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Int: return AnimatorDriverTask.ParameterType.Int;
                    case AnimatorControllerParameterType.Bool: return AnimatorDriverTask.ParameterType.Bool;
                    default: return AnimatorDriverTask.ParameterType.Float;
                }
            }

            foreach (var p in vrcDriver.parameters)
            {
                switch (p.type)
                {
                    case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Set:
                        driver.EnterTasks.Add(new AnimatorDriverTask
                        {
                            op = AnimatorDriverTask.Operator.Set,
                            targetName = p.name,
                            targetType = TypeOf(p.name),
                            aType = AnimatorDriverTask.SourceType.Static,
                            aValue = p.value
                        });
                        break;

                    case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Add:
                        driver.EnterTasks.Add(new AnimatorDriverTask
                        {
                            op = AnimatorDriverTask.Operator.Addition,
                            targetName = p.name,
                            targetType = TypeOf(p.name),
                            aType = AnimatorDriverTask.SourceType.Parameter,
                            aParamType = TypeOf(p.name),
                            aName = p.name,
                            bType = AnimatorDriverTask.SourceType.Static,
                            bValue = p.value
                        });
                        break;

                    case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Random:
                        driver.EnterTasks.Add(new AnimatorDriverTask
                        {
                            op = AnimatorDriverTask.Operator.Set,
                            targetName = p.name,
                            targetType = TypeOf(p.name),
                            aType = AnimatorDriverTask.SourceType.Random,
                            aValue = TypeOf(p.name) == AnimatorDriverTask.ParameterType.Bool ? 0f : p.valueMin,
                            aMax = TypeOf(p.name) == AnimatorDriverTask.ParameterType.Bool ? 1f : p.valueMax
                        });
                        if (TypeOf(p.name) == AnimatorDriverTask.ParameterType.Bool)
                        {
                            ctx.Report.Approximated(Category, $"Random driver for \"{p.name}\"",
                                "Random bool approximated with a random 0..1 set; chance weighting is not preserved.");
                        }
                        break;

                    case VRC.SDKBase.VRC_AvatarParameterDriver.ChangeType.Copy:
                        if (p.convertRange && !Mathf.Approximately(p.sourceMax - p.sourceMin, 0f))
                        {
                            float scale = (p.destMax - p.destMin) / (p.sourceMax - p.sourceMin);
                            // dst = (src - srcMin) * scale + dstMin, built from chained ops.
                            driver.EnterTasks.Add(new AnimatorDriverTask
                            {
                                op = AnimatorDriverTask.Operator.Subtraction,
                                targetName = p.name,
                                targetType = TypeOf(p.name),
                                aType = AnimatorDriverTask.SourceType.Parameter,
                                aParamType = TypeOf(p.source),
                                aName = p.source,
                                bType = AnimatorDriverTask.SourceType.Static,
                                bValue = p.sourceMin
                            });
                            driver.EnterTasks.Add(new AnimatorDriverTask
                            {
                                op = AnimatorDriverTask.Operator.Multiplication,
                                targetName = p.name,
                                targetType = TypeOf(p.name),
                                aType = AnimatorDriverTask.SourceType.Parameter,
                                aParamType = TypeOf(p.name),
                                aName = p.name,
                                bType = AnimatorDriverTask.SourceType.Static,
                                bValue = scale
                            });
                            driver.EnterTasks.Add(new AnimatorDriverTask
                            {
                                op = AnimatorDriverTask.Operator.Addition,
                                targetName = p.name,
                                targetType = TypeOf(p.name),
                                aType = AnimatorDriverTask.SourceType.Parameter,
                                aParamType = TypeOf(p.name),
                                aName = p.name,
                                bType = AnimatorDriverTask.SourceType.Static,
                                bValue = p.destMin
                            });
                        }
                        else
                        {
                            driver.EnterTasks.Add(new AnimatorDriverTask
                            {
                                op = AnimatorDriverTask.Operator.Set,
                                targetName = p.name,
                                targetType = TypeOf(p.name),
                                aType = AnimatorDriverTask.SourceType.Parameter,
                                aParamType = TypeOf(p.source),
                                aName = p.source
                            });
                        }
                        break;
                }
            }
            return driver.EnterTasks.Count > 0 ? driver : null;
        }

        // ----------------------------------------------------------------- rename ----

        static void RenamePass(AnimatorController master, List<AnimatorControllerLayer> vrcLayers, BridgeContext ctx)
        {
            // Menu-driven parameter names like "VF121_Clothing/Bennett Clothes/Cloak/Cloak"
            // break the CCK's controller autogeneration (spaces/slashes). Rename them to
            // clean names derived from their menu label, consistently everywhere.
            var sanitizedNames = new Dictionary<string, string>();
            var takenNames = new HashSet<string>(master.parameters.Select(p => p.name));

            // Renames decided while the menu was built, which are not negotiable: a Joystick2D
            // addresses its axes as "<machineName>-x" and "-y", so the avatar's own axis
            // parameters have to arrive under exactly those names or the control drives nothing.
            //
            // Not negotiable — but not unconditional either. This map is consulted BEFORE
            // ParameterRenameMap in Rename(), so an entry here shadows the VRChat→ChilloutVR core
            // translations, and it renames whatever it names on the whole controller. Two things
            // must therefore never get in: a key the game itself drives or this tool translates
            // (renaming "Viseme" away means no viseme ever reaches the avatar), and a value that
            // collides with a parameter the controller already declares (the "rename" would merge
            // two unrelated parameters into one). Either would trade a working core system for a
            // joystick, silently. Skip and say so instead.
            foreach (var forced in ctx.ForcedRenames)
            {
                if (CvrCoreParameters.Contains(forced.Key) || StreamFedParameters.Contains(forced.Key)
                    || ParameterRenameMap.ContainsKey(forced.Key))
                {
                    ctx.Report.Warning(Category, $"Refused to rename \"{forced.Key}\"",
                        $"A menu control asked for it to become \"{forced.Value}\", but the game drives " +
                        "this parameter itself — renaming it would disconnect that system. The control " +
                        "keeps its original parameters instead.");
                    continue;
                }
                if (takenNames.Contains(forced.Value))
                {
                    ctx.Report.Warning(Category, $"Refused to rename \"{forced.Key}\" to \"{forced.Value}\"",
                        "A parameter with the target name already exists, so the rename would have merged " +
                        "two unrelated parameters. The control keeps its original parameters instead.");
                    continue;
                }
                sanitizedNames[forced.Key] = forced.Value;
                takenNames.Add(forced.Value);
            }
            foreach (var entry in ctx.CvrAvatar.avatarSettings.settings)
            {
                string machineName = entry.machineName;
                if (string.IsNullOrEmpty(machineName) || sanitizedNames.ContainsKey(machineName))
                {
                    continue;
                }
                if (machineName.IndexOfAny(new[] { ' ', '/', '\\', '(', ')', '<', '>', '\'', '"', ',' }) < 0)
                {
                    continue; // already CCK-safe
                }
                string clean = SanitizeParameterName(string.IsNullOrEmpty(entry.name) ? machineName : entry.name);
                string candidate = clean;
                int suffix = 2;
                while (takenNames.Contains(candidate) || sanitizedNames.ContainsValue(candidate))
                {
                    candidate = clean + suffix++;
                }
                sanitizedNames[machineName] = candidate;
                ctx.Report.Converted(Category, $"Parameter \"{machineName}\"",
                    $"Renamed to CCK-safe \"{candidate}\".");
            }

            string Rename(string name)
            {
                if (string.IsNullOrEmpty(name))
                {
                    return name;
                }
                string result = sanitizedNames.TryGetValue(name, out var sanitized) ? sanitized
                    : ParameterRenameMap.TryGetValue(name, out var mapped) ? mapped : name;
                // Stream-fed parameters must stay synced, and this is the only place that decides
                // it. A CVRParameterStream exists solely on the wearer's own copy of the avatar —
                // it sits on ChilloutVR's local-component whitelist, so the filter strips it from
                // everyone else's. The value it computes reaches other players only by being
                // replicated, and a "#" name is never replicated. Prefixed, MuteSelf and VRMode and
                // the gesture weights and Upright sat frozen at their defaults for every remote
                // viewer: a mute indicator only its wearer could see. Left unprefixed, the wearer's
                // stream writes through ChangeAnimatorParam, which both sets the parameter and
                // broadcasts the change. Costs 32 bits each, and only for parameters the avatar
                // actually declares — CreateParameterStreams builds an entry for nothing else.
                bool preserved = CvrCoreParameters.Contains(result) ||
                                 StreamFedParameters.Contains(result) ||
                                 ctx.PreserveParameters.Contains(name) ||
                                 ctx.PreserveParameters.Contains(result) ||
                                 ctx.ContactParameters.Contains(name);
                if (ctx.Settings.preserveParameterSyncState && !preserved)
                {
                    result = "#" + result;
                }
                // NOTE: menu Buttons used to get a "<impulse=0.1>" suffix here — a ChilloutVR 3
                // era convention. CCK 4 has no such feature, and worse, its Advanced Settings
                // inspector only accepts [a-zA-Z0-9/-_#] in a machine name: the '<', '>', '=' and
                // '.' broke the parameter picker for that entry and every control drawn after it,
                // leaving most of the menu inert. Buttons now convert as plain toggles.
                return result;
            }

            // The only property names a clip may have rewritten. Captured before the parameters
            // are renamed, so these are the pre-rename spellings the clips still use.
            //
            // Humanoid muscle curves are indistinguishable from animated animator parameters by
            // binding alone — both are type Animator with an empty path — so without this filter
            // "Chest Front-Back", "Jaw Close" and "Right Hand.Little.3 Stretched" are treated as
            // parameters, get the "#" local prefix, and bind to nothing. Every muscle curve in
            // the clip dies silently and the avatar holds its rest pose while IK-tracked parts
            // carry on, which is exactly how it looks in the animation window: rows of
            // "Animator.#Chest Front-Back (Missing!)".
            var animatableParameters = new HashSet<string>(master.parameters.Select(p => p.name));

            // Parameters (dedupe after rename; e.g. Viseme folds into VisemeIdx).
            var newParams = new List<AnimatorControllerParameter>();
            var seenNames = new HashSet<string>();
            foreach (var param in master.parameters)
            {
                string newName = Rename(param.name);
                if (!seenNames.Add(newName))
                {
                    continue;
                }
                if (newName != param.name)
                {
                    param.name = newName;
                }
                newParams.Add(param);
            }
            master.parameters = newParams.ToArray();

            var clipMap = new Dictionary<AnimationClip, AnimationClip>();
            // Every state machine in the controller, not just the VRChat layers being merged.
            // The rename map only ever contains VRChat parameter names, so applying it to the
            // CCK's own layers is a no-op there — but a reference living outside vrcLayers used
            // to keep the ORIGINAL name while the declaration moved to the CCK-safe one, which
            // is a reference to a parameter that no longer exists. A quad avatar lost its leg
            // tracking and grounder that way: "#Controls/Synced/LegsOffset_Hind" still named in
            // transitions while the parameter had become "LegsOffsetHind".
            var machines = master.layers.Select(l => l.stateMachine)
                .Concat(vrcLayers.Select(l => l.stateMachine))
                .Where(m => m != null)
                .Distinct();
            foreach (var stateMachine in machines)
            {
                WalkMachines(stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        state.timeParameter = Rename(state.timeParameter);
                        state.speedParameter = Rename(state.speedParameter);
                        state.mirrorParameter = Rename(state.mirrorParameter);
                        state.cycleOffsetParameter = Rename(state.cycleOffsetParameter);
                        state.motion = RenameInMotion(state.motion, Rename, clipMap, ctx, animatableParameters);

                        foreach (var behaviour in state.behaviours)
                        {
                            RenameInDriver(behaviour as AnimatorDriver, Rename);
                        }
                        RenameConditions(child.state.transitions, Rename);
                    }
                    RenameConditions(machine.anyStateTransitions, Rename);
                    RenameConditions(machine.entryTransitions, Rename);
                    foreach (var behaviour in machine.behaviours)
                    {
                        RenameInDriver(behaviour as AnimatorDriver, Rename);
                    }
                });
            }

            // Advanced settings + triggers created earlier also need matching names.
            foreach (var setting in ctx.CvrAvatar.avatarSettings.settings)
            {
                setting.machineName = Rename(setting.machineName);
            }
            foreach (var trigger in ctx.CvrAvatar.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true))
            {
                trigger.settingName = Rename(trigger.settingName);
                foreach (var task in trigger.enterTasks) task.settingName = Rename(task.settingName);
                foreach (var task in trigger.exitTasks) task.settingName = Rename(task.settingName);
                foreach (var task in trigger.stayTasks) task.settingName = Rename(task.settingName);
            }

            if (clipMap.Count > 0)
            {
                ctx.Report.Converted(Category, $"{clipMap.Count} animation clips cloned",
                    "They animate renamed animator parameters (animated animator parameters).");
            }
        }

        static void RenameConditions(AnimatorTransitionBase[] transitions, Func<string, string> rename)
        {
            foreach (var transition in transitions)
            {
                var conditions = transition.conditions;
                bool changed = false;
                for (int i = 0; i < conditions.Length; i++)
                {
                    string newName = rename(conditions[i].parameter);
                    if (newName != conditions[i].parameter)
                    {
                        conditions[i].parameter = newName;
                        changed = true;
                    }
                }
                if (changed)
                {
                    transition.conditions = conditions;
                }
            }
        }

        static void RenameInDriver(AnimatorDriver driver, Func<string, string> rename)
        {
            if (driver == null)
            {
                return;
            }
            foreach (var task in driver.EnterTasks) RenameTask(task, rename);
            foreach (var task in driver.ExitTasks) RenameTask(task, rename);
        }

        static void RenameTask(AnimatorDriverTask task, Func<string, string> rename)
        {
            task.targetName = rename(task.targetName);
            if (task.aType == AnimatorDriverTask.SourceType.Parameter) task.aName = rename(task.aName);
            if (task.bType == AnimatorDriverTask.SourceType.Parameter) task.bName = rename(task.bName);
        }

        static Motion RenameInMotion(Motion motion, Func<string, string> rename,
            Dictionary<AnimationClip, AnimationClip> clipMap, BridgeContext ctx, HashSet<string> animatable)
        {
            if (motion is BlendTree tree)
            {
                tree.blendParameter = rename(tree.blendParameter);
                if (tree.blendType != BlendTreeType.Simple1D && tree.blendType != BlendTreeType.Direct)
                {
                    tree.blendParameterY = rename(tree.blendParameterY);
                }
                var children = tree.children;
                for (int i = 0; i < children.Length; i++)
                {
                    if (tree.blendType == BlendTreeType.Direct)
                    {
                        children[i].directBlendParameter = rename(children[i].directBlendParameter);
                    }
                    children[i].motion = RenameInMotion(children[i].motion, rename, clipMap, ctx, animatable);
                }
                tree.children = children;
                return tree;
            }

            if (motion is AnimationClip clip)
            {
                return RenameInClip(clip, rename, clipMap, animatable);
            }
            return motion;
        }

        /// <summary>
        /// Clips can animate animator parameters directly (AAPs). Those bindings live on
        /// the shared clip asset, so a renamed parameter forces a clone-on-write copy.
        /// </summary>
        static AnimationClip RenameInClip(AnimationClip clip, Func<string, string> rename,
            Dictionary<AnimationClip, AnimationClip> clipMap, HashSet<string> animatable)
        {
            if (clip == null)
            {
                return null;
            }
            if (clipMap.TryGetValue(clip, out var existing))
            {
                return existing;
            }

            var bindings = AnimationUtility.GetCurveBindings(clip);
            var renames = bindings
                .Where(b => b.type == typeof(Animator) && string.IsNullOrEmpty(b.path) &&
                            // Must actually be a parameter. Muscle and root curves share this
                            // binding shape exactly, and renaming one destroys it.
                            animatable.Contains(b.propertyName) &&
                            rename(b.propertyName) != b.propertyName)
                .ToArray();
            if (renames.Length == 0)
            {
                clipMap[clip] = clip;
                return clip;
            }

            var clone = UnityEngine.Object.Instantiate(clip);
            clone.name = clip.name + "_cvr";
            clone.hideFlags = HideFlags.None;
            foreach (var binding in renames)
            {
                var curve = AnimationUtility.GetEditorCurve(clone, binding);
                AnimationUtility.SetEditorCurve(clone, binding, null);
                var newBinding = binding;
                newBinding.propertyName = rename(binding.propertyName);
                AnimationUtility.SetEditorCurve(clone, newBinding, curve);
            }
            clipMap[clip] = clone;
            return clone;
        }

        // ----------------------------------------------------------------- extras ----

        static void ApplyParameterDefaults(AnimatorController master, BridgeContext ctx)
        {
            var vrcParams = ctx.SourceDescriptor.expressionParameters;
            var defaults = new Dictionary<string, float>();
            if (vrcParams != null && vrcParams.parameters != null)
            {
                foreach (var p in vrcParams.parameters)
                {
                    if (!string.IsNullOrEmpty(p.name))
                    {
                        defaults[p.name] = p.defaultValue;
                    }
                }
            }

            var unsupportedPresent = new List<string>();
            var parameters = master.parameters;
            foreach (var param in parameters)
            {
                string bareName = param.name.TrimStart('#');
                if (defaults.TryGetValue(bareName, out var value))
                {
                    param.defaultFloat = value;
                    param.defaultInt = (int)value;
                    param.defaultBool = value != 0;
                }
                else if (NonZeroDefaults.TryGetValue(bareName, out var coreDefault) &&
                         param.type == AnimatorControllerParameterType.Float &&
                         Mathf.Approximately(param.defaultFloat, 0f))
                {
                    param.defaultFloat = coreDefault;
                }
                if (KnownUnsupportedVrcParameters.Contains(bareName) && bareName != "VelocityMagnitude")
                {
                    unsupportedPresent.Add(bareName);
                }
            }
            master.parameters = parameters;

            if (unsupportedPresent.Count > 0)
            {
                ctx.Report.Skipped(Category, "VRC built-in parameters without CVR equivalent",
                    string.Join(", ", unsupportedPresent.Distinct()) + " — nothing writes them in game, so " +
                    "they hold one value forever. Those with a known resting reading are set to it (see " +
                    "\"given a resting value\" below); the rest sit at 0, which is correct for them.");
            }
        }


        /// <summary>
        /// A parameter can end up one type while some transitions still condition on it with a
        /// comparison that type can't express — most often a Float/Bool type conflict that
        /// "keeps Float" but leaves bool-style If/IfNot conditions behind (this is what breaks
        /// the DSR face-tracking rig's RemoteModeActive local/remote gate). ChilloutVR's CCK
        /// rejects such transitions ("parameter ... not compatible with condition type") and the
        /// state silently never switches. Rewrite every condition's mode to match its
        /// parameter's final type. Only invalid mode/type pairings are touched.
        /// </summary>
        /// <summary>
        /// Brings every generated AnimatorDriver task's targetType into line with the
        /// parameter's FINAL declared type.
        ///
        /// ConvertParameterDriver reads the type while the driver is built, in BehaviourPass —
        /// which runs BEFORE ParameterTypeInference turns VRCFury's all-float parameters into
        /// real bools and ints. Every driver written before that retyping kept "Float" for what
        /// is now a Bool, and the decompiled client shows why that is invisible until it isn't
        /// (AnimatorDriverTask.ApplyResult):
        ///
        ///   * in game on the LOCAL avatar the type is ignored — the value goes through
        ///     PlayerSetup.ChangeAnimatorParam and the animator manager coerces it to the
        ///     declared type, so the driver fires;
        ///   * everywhere else — including Unity play mode, where the driver resolves to
        ///     MiscAnimator — it calls Animator.SetFloat on a Bool parameter, which Unity
        ///     SILENTLY IGNORES.
        ///
        /// A mistyped driver therefore does nothing in the editor and everything in game: the
        /// exact "works in Unity, breaks in game" shape, and it hid driver faults from the CCK
        /// Animator Tester as well.
        /// </summary>
        static void SyncDriverParameterTypes(AnimatorController master, BridgeContext ctx)
        {
            var types = new Dictionary<string, AnimatorControllerParameterType>();
            foreach (var p in master.parameters)
            {
                types[p.name] = p.type;
            }

            int corrected = 0;
            var names = new List<string>();
            void FixTasks(List<AnimatorDriverTask> tasks)
            {
                if (tasks == null)
                {
                    return;
                }
                foreach (var task in tasks)
                {
                    if (task == null || string.IsNullOrEmpty(task.targetName) ||
                        !types.TryGetValue(task.targetName, out var type))
                    {
                        continue;
                    }
                    AnimatorDriverTask.ParameterType want;
                    switch (type)
                    {
                        case AnimatorControllerParameterType.Int: want = AnimatorDriverTask.ParameterType.Int; break;
                        case AnimatorControllerParameterType.Bool: want = AnimatorDriverTask.ParameterType.Bool; break;
                        case AnimatorControllerParameterType.Trigger: want = AnimatorDriverTask.ParameterType.Trigger; break;
                        default: want = AnimatorDriverTask.ParameterType.Float; break;
                    }
                    if (task.targetType != want)
                    {
                        task.targetType = want;
                        corrected++;
                        if (names.Count < 6 && !names.Contains(task.targetName))
                        {
                            names.Add(task.targetName);
                        }
                    }
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var behaviour in machine.behaviours)
                    {
                        if (behaviour is AnimatorDriver machineDriver)
                        {
                            FixTasks(machineDriver.EnterTasks);
                            FixTasks(machineDriver.ExitTasks);
                        }
                    }
                    foreach (var child in machine.states)
                    {
                        foreach (var behaviour in child.state.behaviours)
                        {
                            if (behaviour is AnimatorDriver driver)
                            {
                                FixTasks(driver.EnterTasks);
                                FixTasks(driver.ExitTasks);
                            }
                        }
                    }
                });
            }

            if (corrected > 0)
            {
                ctx.Report.Converted(Category,
                    $"{corrected} parameter-driver task(s) retyped to match their parameter",
                    $"{string.Join(", ", names)}{(corrected > names.Count ? ", …" : "")} — the drivers were " +
                    "built before the parameters were retyped from VRCFury's floats into real bools and ints, " +
                    "so they carried the old type. ChilloutVR ignores the type on your own avatar (it coerces " +
                    "to the declared type) but obeys it everywhere else, and Unity ignores a float write to a " +
                    "bool parameter entirely — which is why a mistyped driver could look dead in the editor " +
                    "while firing in game.");
            }
        }

        /// <summary>
        /// Removes AnyState transitions that make a driver-bearing state unconditional.
        ///
        /// Found on a tester's avatar: VRCFury's "exclusive tag" layer had, for each toggle, TWO
        /// AnyState transitions to the same state — one "parameter is true" and one "parameter is
        /// false" — which together fire no matter what. Unity evaluates AnyState transitions in
        /// order and skips one whose destination is the current state (canTransitionToSelf off),
        /// so the layer walked to the NEXT toggle's state instead, ran that state's driver, and
        /// that driver switches the other toggles OFF. The result is a permanent ping-pong: every
        /// frame a different exclusive state is entered and zeroes its siblings, so a toggle
        /// pressed in the quick menu turns itself back off instantly — a visible flicker with the
        /// parameter genuinely flipping, which is exactly what the CCK Debugger showed.
        ///
        /// The pair is only repaired where it is provably harmful — the destination runs a
        /// parameter driver — and only the "is false" half is dropped, which leaves the reading
        /// every exclusive-tag layer intends: enter this toggle's state when this toggle is on.
        /// </summary>
        static void RepairUnconditionalDriverStates(AnimatorController master, BridgeContext ctx)
        {
            int removed = 0;
            var notes = new List<string>();

            bool RunsDriver(AnimatorState state) =>
                state != null && state.behaviours != null &&
                state.behaviours.Any(b => b is AnimatorDriver driver &&
                    ((driver.EnterTasks != null && driver.EnterTasks.Count > 0) ||
                     (driver.ExitTasks != null && driver.ExitTasks.Count > 0)));

            // The single condition a transition rests on, or null when it has none or several.
            AnimatorCondition? SoleCondition(AnimatorStateTransition transition) =>
                transition.conditions != null && transition.conditions.Length == 1
                    ? transition.conditions[0]
                    : (AnimatorCondition?)null;

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    var transitions = machine.anyStateTransitions;
                    if (transitions == null || transitions.Length < 2)
                    {
                        return;
                    }
                    var doomed = new HashSet<AnimatorStateTransition>();
                    foreach (var negative in transitions)
                    {
                        var negativeCondition = SoleCondition(negative);
                        if (negativeCondition == null ||
                            negativeCondition.Value.mode != AnimatorConditionMode.IfNot ||
                            !RunsDriver(negative.destinationState))
                        {
                            continue;
                        }
                        // Is the same destination also entered when the parameter IS true?
                        bool complemented = transitions.Any(other =>
                        {
                            if (ReferenceEquals(other, negative) ||
                                other.destinationState != negative.destinationState)
                            {
                                return false;
                            }
                            var condition = SoleCondition(other);
                            return condition != null &&
                                   condition.Value.mode == AnimatorConditionMode.If &&
                                   condition.Value.parameter == negativeCondition.Value.parameter;
                        });
                        if (complemented)
                        {
                            doomed.Add(negative);
                            if (notes.Count < 5)
                            {
                                notes.Add($"\"{negative.destinationState.name}\" on {negativeCondition.Value.parameter}");
                            }
                        }
                    }
                    if (doomed.Count > 0)
                    {
                        machine.anyStateTransitions = transitions.Where(t => !doomed.Contains(t)).ToArray();
                        removed += doomed.Count;
                    }
                });
            }

            if (removed > 0)
            {
                ctx.Report.Approximated(Category,
                    $"{removed} always-true transition(s) into parameter-driving state(s) removed",
                    $"{string.Join("; ", notes)}{(removed > notes.Count ? "; …" : "")} — each of these states " +
                    "was entered both when its parameter was true AND when it was false, which together means " +
                    "\"always\". Because Unity skips an AnyState transition that points at the state it is " +
                    "already in, the layer stepped to the NEXT such state every frame and ran ITS driver, and " +
                    "those drivers switch the sibling toggles off — so a toggle pressed in the menu flipped " +
                    "straight back off (a flicker, with the parameter really changing). The \"is false\" half " +
                    "is dropped, leaving what an exclusive-clothing layer means: enter this toggle's state " +
                    "when this toggle is on. Check that switching between these options behaves.");
            }
        }

        static void ReconcileConditionModes(AnimatorController master, BridgeContext ctx)
        {
            var types = new Dictionary<string, AnimatorControllerParameterType>();
            foreach (var p in master.parameters)
            {
                types[p.name] = p.type;
            }

            int fixedCount = 0;
            var touched = new HashSet<string>();

            void Reconcile(AnimatorTransitionBase[] transitions)
            {
                foreach (var transition in transitions)
                {
                    if (transition == null) continue;
                    var conditions = transition.conditions;
                    bool changed = false;
                    for (int i = 0; i < conditions.Length; i++)
                    {
                        if (!types.TryGetValue(conditions[i].parameter, out var type))
                        {
                            continue;
                        }
                        var mode = conditions[i].mode;
                        float threshold = conditions[i].threshold;
                        var newMode = mode;
                        float newThreshold = threshold;

                        switch (type)
                        {
                            // Bool / Trigger accept only If / IfNot.
                            case AnimatorControllerParameterType.Bool:
                            case AnimatorControllerParameterType.Trigger:
                                switch (mode)
                                {
                                    case AnimatorConditionMode.If:
                                    case AnimatorConditionMode.IfNot:
                                        break;
                                    case AnimatorConditionMode.Greater:
                                        newMode = AnimatorConditionMode.If; newThreshold = 0f; break;
                                    case AnimatorConditionMode.Less:
                                        newMode = AnimatorConditionMode.IfNot; newThreshold = 0f; break;
                                    case AnimatorConditionMode.Equals:
                                        newMode = threshold != 0f ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                                        newThreshold = 0f; break;
                                    case AnimatorConditionMode.NotEqual:
                                        newMode = threshold != 0f ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If;
                                        newThreshold = 0f; break;
                                }
                                break;

                            // Float accepts only Greater / Less.
                            case AnimatorControllerParameterType.Float:
                                switch (mode)
                                {
                                    case AnimatorConditionMode.Greater:
                                    case AnimatorConditionMode.Less:
                                        break;
                                    case AnimatorConditionMode.If:
                                        newMode = AnimatorConditionMode.Greater; newThreshold = 0.5f; break;
                                    case AnimatorConditionMode.IfNot:
                                        newMode = AnimatorConditionMode.Less; newThreshold = 0.5f; break;
                                    case AnimatorConditionMode.Equals:
                                        newMode = AnimatorConditionMode.Greater; newThreshold = 0.5f; break;
                                    case AnimatorConditionMode.NotEqual:
                                        newMode = AnimatorConditionMode.Less; newThreshold = 0.5f; break;
                                }
                                break;

                            // Int accepts Greater / Less / Equals / NotEqual (not If / IfNot).
                            case AnimatorControllerParameterType.Int:
                                switch (mode)
                                {
                                    case AnimatorConditionMode.If:
                                        newMode = AnimatorConditionMode.NotEqual; newThreshold = 0f; break;
                                    case AnimatorConditionMode.IfNot:
                                        newMode = AnimatorConditionMode.Equals; newThreshold = 0f; break;
                                }
                                break;
                        }

                        if (newMode != mode || !Mathf.Approximately(newThreshold, threshold))
                        {
                            conditions[i].mode = newMode;
                            conditions[i].threshold = newThreshold;
                            changed = true;
                            touched.Add(conditions[i].parameter);
                            fixedCount++;
                        }
                    }
                    if (changed)
                    {
                        transition.conditions = conditions;
                    }
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    Reconcile(machine.anyStateTransitions);
                    Reconcile(machine.entryTransitions);
                    foreach (var child in machine.states)
                    {
                        Reconcile(child.state.transitions);
                    }
                });
            }

            if (fixedCount > 0)
            {
                ctx.Report.Converted(Category,
                    $"Reconciled {fixedCount} transition condition(s) to their parameter's type",
                    $"A merge/inject left conditions using a comparison the parameter type can't express " +
                    $"(e.g. a bool-style If on a Float): {string.Join(", ", touched.OrderBy(n => n))}. " +
                    "ChilloutVR rejects those transitions outright, so the states never switch — this is " +
                    "what leaves face-tracking's RemoteModeActive local/remote gate dead.");
            }
        }

        /// <summary>
        /// Characters ChilloutVR accepts in a menu parameter name. Anything else breaks the
        /// CCK's Advanced Settings inspector — its own field sanitiser is
        /// <c>Regex.Replace(name, "[^a-zA-Z0-9/\-_#]", "")</c>, and feeding the parameter
        /// picker a name outside that set takes out every control drawn after it.
        /// </summary>
        static readonly System.Text.RegularExpressions.Regex IllegalMenuNameChars =
            new System.Text.RegularExpressions.Regex(@"[^a-zA-Z0-9/\-_#]");

        /// <summary>
        /// Last line of defence: renames any parameter whose name ChilloutVR can't accept,
        /// keeping the menu entry and the animator in step. Nothing should reach here — the
        /// rename pass already produces CCK-safe names — but a menu full of dead controls is
        /// an expensive way to find out otherwise, so it's checked rather than assumed.
        /// </summary>
        static void VerifyMenuParameterNames(AnimatorController master, BridgeContext ctx)
        {
            var settings = ctx.CvrAvatar.avatarSettings.settings;
            if (settings == null)
            {
                return;
            }

            var taken = new HashSet<string>(master.parameters.Select(p => p.name));
            var renames = new Dictionary<string, string>();
            foreach (var entry in settings)
            {
                string name = entry?.machineName;
                if (string.IsNullOrEmpty(name) || !IllegalMenuNameChars.IsMatch(name) ||
                    renames.ContainsKey(name))
                {
                    continue;
                }
                string clean = IllegalMenuNameChars.Replace(name, "");
                if (string.IsNullOrEmpty(clean))
                {
                    clean = "Param";
                }
                string candidate = clean;
                int suffix = 2;
                while (taken.Contains(candidate))
                {
                    candidate = clean + suffix++;
                }
                taken.Add(candidate);
                renames[name] = candidate;
            }
            if (renames.Count == 0)
            {
                return;
            }

            string Rename(string n) => n != null && renames.TryGetValue(n, out var r) ? r : n;

            // Hold the array. AnimatorController.parameters hands back a fresh copy on every
            // read, so iterating the property and then assigning the property re-reads an
            // untouched copy and throws the renames away — leaving the DECLARATION under the
            // illegal name while every condition, driver and menu entry below moves to the
            // clean one. Every other parameter edit in this file already does it this way.
            var parameters = master.parameters;
            foreach (var param in parameters)
            {
                param.name = Rename(param.name);
            }
            master.parameters = parameters;

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    RenameConditions(machine.anyStateTransitions, Rename);
                    RenameConditions(machine.entryTransitions, Rename);
                    foreach (var behaviour in machine.behaviours)
                    {
                        RenameInDriver(behaviour as AnimatorDriver, Rename);
                    }
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        state.timeParameter = Rename(state.timeParameter);
                        state.speedParameter = Rename(state.speedParameter);
                        state.mirrorParameter = Rename(state.mirrorParameter);
                        state.cycleOffsetParameter = Rename(state.cycleOffsetParameter);
                        RenameMotionParameters(state.motion, Rename);
                        RenameConditions(state.transitions, Rename);
                        foreach (var behaviour in state.behaviours)
                        {
                            RenameInDriver(behaviour as AnimatorDriver, Rename);
                        }
                    }
                });
            }

            foreach (var entry in settings)
            {
                if (entry != null)
                {
                    entry.machineName = Rename(entry.machineName);
                }
            }
            foreach (var trigger in ctx.CvrAvatar.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true))
            {
                trigger.settingName = Rename(trigger.settingName);
                foreach (var task in trigger.enterTasks) task.settingName = Rename(task.settingName);
                foreach (var task in trigger.exitTasks) task.settingName = Rename(task.settingName);
                foreach (var task in trigger.stayTasks) task.settingName = Rename(task.settingName);
            }
            EditorUtility.SetDirty(ctx.CvrAvatar);

            foreach (var pair in renames)
            {
                ctx.Report.Warning(Category, $"Parameter \"{pair.Key}\" renamed to \"{pair.Value}\"",
                    "ChilloutVR only accepts letters, digits, '/', '-', '_' and '#' in a menu parameter " +
                    "name; the original would have broken the Advanced Settings inspector.");
            }
        }

        /// <summary>Renames blend-tree parameters in place (no clip rebinding — names only).</summary>
        static void RenameMotionParameters(Motion motion, Func<string, string> rename)
        {
            if (!(motion is BlendTree tree))
            {
                return;
            }
            tree.blendParameter = rename(tree.blendParameter);
            tree.blendParameterY = rename(tree.blendParameterY);
            var children = tree.children;
            for (int i = 0; i < children.Length; i++)
            {
                children[i].directBlendParameter = rename(children[i].directBlendParameter);
                RenameMotionParameters(children[i].motion, rename);
            }
            tree.children = children;
        }

        /// <summary>
        /// Drops menu entries whose parameter doesn't exist on the final controller.
        ///
        /// A VRChat avatar can declare an expression parameter — and give it a menu control —
        /// that no converted animator layer actually reads: it belonged to a playable layer
        /// that wasn't converted (Action emotes, VRCEmote/VRCFaceBlend*), or to a system that
        /// was stripped. Those entries are inert clutter: the menu shows a control, nothing
        /// listens, and the CCK inspector flags the missing parameter in red. Run last, once
        /// every rename and injection has settled, so names are final.
        /// </summary>
        static void PruneDeadMenuEntries(AnimatorController master, BridgeContext ctx)
        {
            var settings = ctx.CvrAvatar.avatarSettings.settings;
            if (settings == null || settings.Count == 0)
            {
                return;
            }
            var known = new HashSet<string>(master.parameters.Select(p => p.name));
            // A parameter can exist and still be inert: nothing conditions on it, no blend
            // tree or clip touches it, no driver reads or writes it. One test avatar carried a
            // 64-option instrument dropdown like this — the parameter survived because the
            // layers that used it didn't, so the entry looked alive while doing nothing.
            var referenced = CollectReferencedParameters(master);

            // A joystick's machineName is not itself a parameter — ChilloutVR derives one per
            // axis, "<machineName>-x" and "-y" (and "-z" for the 3D one). Judging those entries
            // by their bare name condemns every one of them: the name never appears in the
            // animator, so a working Joystick2D looked exactly like a dead entry and was removed,
            // taking the only control for those axes with it.
            string[] AxesOf(ABI.CCK.Scripts.CVRAdvancedSettingsEntry entry)
            {
                switch (entry.type)
                {
                    case ABI.CCK.Scripts.CVRAdvancedSettingsEntry.SettingsType.Joystick2D:
                    case ABI.CCK.Scripts.CVRAdvancedSettingsEntry.SettingsType.InputVector2:
                        return new[] { entry.machineName + "-x", entry.machineName + "-y" };
                    case ABI.CCK.Scripts.CVRAdvancedSettingsEntry.SettingsType.Joystick3D:
                    case ABI.CCK.Scripts.CVRAdvancedSettingsEntry.SettingsType.InputVector3:
                        return new[]
                        {
                            entry.machineName + "-x", entry.machineName + "-y", entry.machineName + "-z"
                        };
                    default:
                        return new[] { entry.machineName };
                }
            }

            // Alive if any axis is declared and actually read. Any, not all: a puppet whose
            // avatar only animates one axis is still a control worth keeping.
            var dead = settings
                .Where(e => e != null && !string.IsNullOrEmpty(e.machineName)
                            && !AxesOf(e).Any(n => known.Contains(n) && referenced.Contains(n)))
                .ToList();
            if (dead.Count == 0)
            {
                return;
            }

            foreach (var entry in dead)
            {
                settings.Remove(entry);
                ctx.Report.Skipped(Category, $"Menu entry \"{entry.name}\" removed",
                    $"Nothing in the converted animator {(known.Contains(entry.machineName) ? "reads or writes" : "declares")} " +
                    $"\"{entry.machineName}\" — the parameter belongs to a layer that wasn't converted " +
                    "(Action/emotes) or to a stripped system, so the control would have sat in your menu " +
                    "doing nothing.");
            }
            EditorUtility.SetDirty(ctx.CvrAvatar);
            ctx.Report.Converted(Category, $"Removed {dead.Count} dead menu entr(ies)",
                "Their parameter is either missing from the animator or present but never read, written or " +
                "compared anywhere in it, so they could never have done anything.");
        }

        /// <summary>
        /// Removes the placeholder entries from int dropdowns by renumbering the parameter.
        ///
        /// ChilloutVR addresses dropdown options by POSITION — option 20 sets the parameter
        /// to 20 — so a VRChat menu that used sparse values (say 1, 2 and 20) forces 21
        /// entries, 18 of which do nothing. The option list can't be thinned on its own
        /// without silently re-pointing every entry after the gap.
        ///
        /// But nothing requires the animator to keep the original numbers. Renumbering the
        /// values it actually uses down to 0..N-1 — conditions, drivers and triggers
        /// together — makes the dropdown exactly as long as it has real options, with no
        /// placeholders at all and identical behaviour.
        ///
        /// Only safe for parameters used purely as discrete selectors, so anything treating
        /// the value as a quantity (blend trees, motion time, arithmetic drivers, clips
        /// writing it) disqualifies it and the padded list is kept.
        /// </summary>
        static void CompactIntDropdowns(AnimatorController master, BridgeContext ctx)
        {
            var settings = ctx.CvrAvatar.avatarSettings.settings;
            if (settings == null)
            {
                return;
            }

            int compactedEntries = 0, removedOptions = 0;
            foreach (var entry in settings)
            {
                if (entry == null ||
                    !(entry.setting is ABI.CCK.Scripts.CVRAdvancesAvatarSettingGameObjectDropdown dropdown) ||
                    dropdown.options == null || dropdown.options.Count <= 2)
                {
                    continue;
                }
                var declared = master.parameters.FirstOrDefault(p => p.name == entry.machineName);
                if (declared == null || declared.type != AnimatorControllerParameterType.Int)
                {
                    continue;
                }

                string param = entry.machineName;
                if (!TryCollectSelectorValues(master, param, out var use, out string blockedBy))
                {
                    // Used as a quantity somewhere, so renumbering would change behaviour.
                    // The placeholders have to stay — say so rather than leave them unexplained.
                    int stuck = dropdown.options.Count(o => o != null && o.name == ParameterMenuConverter.UnusedOption);
                    if (stuck > 0)
                    {
                        ctx.Report.Approximated(Category, $"Dropdown \"{entry.name}\" keeps {stuck} \"{ParameterMenuConverter.UnusedOption}\" option(s)",
                            $"ChilloutVR selects dropdown options by position, so \"{param}\" needs an entry per " +
                            $"value up to its highest. It's normally renumbered to close those gaps, but {blockedBy} " +
                            "— renumbering would change how the avatar behaves. Deleting the spare entries by hand " +
                            "would shift every option after them onto the wrong value.");
                    }
                    continue;
                }
                var oldOptions = dropdown.options;
                var kept = new HashSet<int>(use.Exact) { 0, dropdown.defaultValue };

                // Values distinguished only by a "> t" or "< t" are interchangeable to the
                // animator, but the menu still has to be able to reach that side of the
                // boundary. Keep one option from each side that isn't already covered,
                // preferring a named one over a placeholder so the label still means something.
                int PickRepresentative(int from, int to)
                {
                    from = Mathf.Max(0, from);
                    to = Mathf.Min(oldOptions.Count - 1, to);
                    int fallback = -1;
                    for (int v = from; v <= to; v++)
                    {
                        if (oldOptions[v] == null)
                        {
                            continue;
                        }
                        if (fallback < 0)
                        {
                            fallback = v;
                        }
                        if (oldOptions[v].name != ParameterMenuConverter.UnusedOption)
                        {
                            return v;
                        }
                    }
                    return fallback;
                }
                foreach (int t in use.GreaterCuts)
                {
                    if (!kept.Any(v => v > t))
                    {
                        int rep = PickRepresentative(t + 1, oldOptions.Count - 1);
                        if (rep >= 0) kept.Add(rep);
                    }
                }
                foreach (int t in use.LessCuts)
                {
                    if (!kept.Any(v => v < t))
                    {
                        int rep = PickRepresentative(0, t - 1);
                        if (rep >= 0) kept.Add(rep);
                    }
                }

                var ordered = kept.Where(v => v >= 0 && v < oldOptions.Count).Distinct().OrderBy(v => v).ToList();
                if (ordered.Count == 0 || ordered.Count >= oldOptions.Count)
                {
                    continue; // already dense — nothing to gain
                }

                // Order-preserving, so relative comparisons keep their meaning.
                var map = new Dictionary<int, int>();
                for (int i = 0; i < ordered.Count; i++)
                {
                    map[ordered[i]] = i;
                }

                RemapIntConditions(master, param, map, ordered);
                RemapIntDrivers(master, param, map);
                RemapTriggerValues(ctx, param, map);

                dropdown.options = ordered.Select(v => oldOptions[v]).ToList();
                dropdown.defaultValue = map.TryGetValue(dropdown.defaultValue, out var newDefault) ? newDefault : 0;
                declared.defaultInt = dropdown.defaultValue;
                declared.defaultFloat = dropdown.defaultValue;

                removedOptions += oldOptions.Count - dropdown.options.Count;
                compactedEntries++;
                ctx.Report.Converted(Category, $"Dropdown \"{entry.name}\" compacted",
                    $"{oldOptions.Count} options -> {dropdown.options.Count}; the parameter's values were " +
                    "renumbered to match, so every entry now does something.");
            }

            if (compactedEntries > 0)
            {
                EditorUtility.SetDirty(ctx.CvrAvatar);
                ctx.Report.Converted(Category,
                    $"Removed {removedOptions} placeholder dropdown option(s) across {compactedEntries} menu entr(ies)",
                    "ChilloutVR dropdowns select by position, so gaps in the VRChat parameter's values would " +
                    "otherwise appear as dead entries; the animator was renumbered instead.");
            }
        }

        /// <summary>
        /// Collects the values an int parameter is used with, or returns false if anything
        /// treats it as a quantity rather than a discrete selection — in which case
        /// renumbering it would change how the avatar behaves.
        /// </summary>
        /// <summary>
        /// How a dropdown parameter is used: the values it is matched against exactly, and the
        /// boundaries it is compared across.
        /// </summary>
        class SelectorUse
        {
            public readonly HashSet<int> Exact = new HashSet<int>();
            public readonly HashSet<int> GreaterCuts = new HashSet<int>();  // "> t"
            public readonly HashSet<int> LessCuts = new HashSet<int>();     // "< t"
        }

        /// <summary>
        /// Gathers what a dropdown parameter is compared against, or fails if renumbering it
        /// could change behaviour.
        ///
        /// Greater/Less used to fail here, which is why avatars kept dropdowns full of
        /// "(unused)" entries — one had 30 options for 10 real ones, another 256 for 13. They
        /// don't have to: the compaction map is order-preserving, so a "&gt;" still partitions
        /// the same values as long as its threshold moves with them. What genuinely can't
        /// survive renumbering is arithmetic — a driver adding to the value, or reading it as
        /// an operand — and quantity reads like blend trees and motion time.
        /// </summary>
        static bool TryCollectSelectorValues(AnimatorController master, string param,
            out SelectorUse use, out string blockedBy)
        {
            var found = new SelectorUse();
            use = found;
            bool safe = true;
            string reason = null;

            bool CollectTransitions(AnimatorTransitionBase[] transitions)
            {
                foreach (var transition in transitions)
                {
                    foreach (var condition in transition.conditions)
                    {
                        if (condition.parameter != param)
                        {
                            continue;
                        }
                        int threshold = Mathf.RoundToInt(condition.threshold);
                        switch (condition.mode)
                        {
                            case AnimatorConditionMode.Equals:
                            case AnimatorConditionMode.NotEqual:
                                found.Exact.Add(threshold);
                                break;
                            case AnimatorConditionMode.Greater:
                                found.GreaterCuts.Add(threshold);
                                break;
                            case AnimatorConditionMode.Less:
                                found.LessCuts.Add(threshold);
                                break;
                            default:
                                // If/IfNot on an int: treated as a flag, leave well alone.
                                reason = "a transition treats it as an on/off flag rather than a selector";
                                return false;
                        }
                    }
                }
                return true;
            }

            bool CollectDriver(StateMachineBehaviour behaviour)
            {
                if (!(behaviour is AnimatorDriver driver))
                {
                    return true;
                }
                foreach (var task in driver.EnterTasks.Concat(driver.ExitTasks))
                {
                    // Read as an operand: whatever it's feeding expects the original numbers.
                    if (task.aName == param || task.bName == param)
                    {
                        reason = "a driver reads it as an operand, so its numbers are passed on elsewhere";
                        return false;
                    }
                    if (task.targetName != param)
                    {
                        continue;
                    }
                    if (task.op != AnimatorDriverTask.Operator.Set)
                    {
                        reason = "a driver does arithmetic on it, where the numbers themselves matter";
                        return false;
                    }
                    if (task.aType != AnimatorDriverTask.SourceType.Static)
                    {
                        // Set from another parameter: that source would need renumbering too.
                        reason = "a driver sets it from another parameter, which would keep the old numbering";
                        return false;
                    }
                    found.Exact.Add(Mathf.RoundToInt(task.aValue));
                }
                return true;
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    if (!safe)
                    {
                        return;
                    }
                    if (!CollectTransitions(machine.anyStateTransitions) ||
                        !CollectTransitions(machine.entryTransitions))
                    {
                        safe = false;
                        return;
                    }
                    foreach (var behaviour in machine.behaviours)
                    {
                        if (!CollectDriver(behaviour)) { safe = false; return; }
                    }
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        if ((state.timeParameterActive && state.timeParameter == param) ||
                            (state.speedParameterActive && state.speedParameter == param) ||
                            (state.mirrorParameterActive && state.mirrorParameter == param) ||
                            (state.cycleOffsetParameterActive && state.cycleOffsetParameter == param) ||
                            MotionUsesParameter(state.motion, param))
                        {
                            reason = "a blend tree or motion time reads it as a quantity";
                            safe = false;
                            return;
                        }
                        if (!CollectTransitions(state.transitions)) { safe = false; return; }
                        foreach (var behaviour in state.behaviours)
                        {
                            if (!CollectDriver(behaviour)) { safe = false; return; }
                        }
                    }
                });
                if (!safe)
                {
                    blockedBy = reason ?? "the value is used in a way renumbering would change";
                    return false;
                }
            }
            blockedBy = null;
            return true;
        }

        /// <summary>True if a blend tree blends on this parameter, or a clip writes it (AAP).</summary>
        static bool MotionUsesParameter(Motion motion, string param)
        {
            if (motion is BlendTree tree)
            {
                // Same vestigial-field rule as CollectReferencedParameters: Direct trees read
                // neither axis field, 1D trees only X. A leftover "Blend" on a Fury Direct tree
                // must not make a parameter of that name look like a live quantity.
                bool usesX = tree.blendType != BlendTreeType.Direct;
                bool usesY = usesX && tree.blendType != BlendTreeType.Simple1D;
                if ((usesX && tree.blendParameter == param) || (usesY && tree.blendParameterY == param))
                {
                    return true;
                }
                foreach (var child in tree.children)
                {
                    if (child.directBlendParameter == param || MotionUsesParameter(child.motion, param))
                    {
                        return true;
                    }
                }
                return false;
            }
            if (motion is AnimationClip clip)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path) &&
                        binding.propertyName == param)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Every parameter name the controller actually touches — conditions, blend trees,
        /// motion time/speed/mirror/cycle offset, driver targets and operands, and animated
        /// animator parameters written by clips. Anything absent from this cannot affect the
        /// avatar no matter what its menu control does.
        /// </summary>
        /// <summary>
        /// VRChat built-ins ChilloutVR never writes, and the value each should rest at.
        ///
        /// Zero is not "neutral" for these — it is a specific and usually impossible state.
        /// TrackingType 0 means "tracking not yet initialised", which VRChat leaves within a
        /// frame of load; Upright 0 means lying flat on the floor. An avatar that keeps them at
        /// 0 forever is being told something untrue about the player.
        ///
        /// Values are VRChat's own resting readings: TrackingType 3 = head and two hands, the
        /// most common real configuration and, being above zero, "initialised"; Upright 1 =
        /// standing; IsLocal 1 = this is the wearer's own copy; scale 1 = unscaled; eye height
        /// 1.6m = VRChat's default. Parameters whose honest resting value IS zero — AFK,
        /// VelocityMagnitude, GroundProximity, InStation, ScaleModified, Earmuffs, AngularY —
        /// are deliberately absent.
        /// </summary>
        static readonly Dictionary<string, float> UnsupportedBuiltInDefaults = new Dictionary<string, float>
        {
            { "IsLocal", 1f },
            { "TrackingType", 3f },
            { "Upright", 1f },
            { "AvatarVersion", 3f },
            { "IsAnimatorEnabled", 1f },
            { "ScaleFactor", 1f },
            { "ScaleFactorInverse", 1f },
            { "EyeHeightAsPercent", 1f },
            { "EyeHeightAsMeters", 1.6f },
        };

        /// <summary>
        /// Gives those built-ins their resting value when nothing in the animator writes them.
        ///
        /// AvatarBridge already reported these as "kept at their default value", which read like
        /// a safe non-action and was the opposite. A FinalIK quadruped converted cleanly and then
        /// lay flat on the floor: both of its Initialize states were gated on
        /// "#TrackingType &gt; 0", nothing ever raised it above 0, so the drivers that switch its
        /// puppet rig on never ran and 84 blend trees stayed at weight 0. The head still tracked,
        /// because head and face tracking don't route through them.
        ///
        /// Only applied where no layer drives the parameter itself — logic that sets it knows
        /// better than this table does — and each one is named in the report.
        /// </summary>
        static void DefaultUnsupportedBuiltIns(AnimatorController master, BridgeContext ctx)
        {
            var parameters = master.parameters;
            var changed = new List<string>();
            // Both spellings are live: CVR marks local-only parameters with a leading "#", and an
            // avatar can carry the prefixed and unprefixed copy at once.
            foreach (var param in parameters)
            {
                string bare = param.name.StartsWith("#") ? param.name.Substring(1) : param.name;
                if (UnsupportedBuiltInDefaults.TryGetValue(bare, out float value)
                    && ApplyRestingValue(master, param, value))
                {
                    changed.Add($"{param.name} = {value:0.##}");
                }
            }
            if (changed.Count == 0)
            {
                return;
            }
            master.parameters = parameters;
            ctx.Report.Converted(Category, $"{changed.Count} VRChat built-in(s) given a resting value",
                $"{string.Join(", ", changed)} — 0 isn't a neutral starting value for these: TrackingType 0 " +
                "means tracking never initialised and Upright 0 means lying flat, and either can hold a whole " +
                "rig in its rest pose. These are the values VRChat would be reporting. Whatever a parameter " +
                "stream or ChilloutVR itself drives takes over from here; this only decides the first frame.");
        }

        /// <summary>
        /// Sets a parameter's default, unless the animator drives it or it is already non-zero.
        /// </summary>
        static bool ApplyRestingValue(AnimatorController master, AnimatorControllerParameter param, float value)
        {
            // Per type: Unity keeps defaultBool/defaultInt/defaultFloat as three independent
            // fields, so an Int parameter defaulting to 2 still reads defaultFloat == 0. Testing
            // the wrong one silently overwrites a default the author chose.
            bool alreadySet;
            switch (param.type)
            {
                case AnimatorControllerParameterType.Trigger:
                    return false; // A trigger has no resting value to give it.
                case AnimatorControllerParameterType.Bool:
                    alreadySet = param.defaultBool;
                    break;
                case AnimatorControllerParameterType.Int:
                    alreadySet = param.defaultInt != 0;
                    break;
                default:
                    alreadySet = !Mathf.Approximately(param.defaultFloat, 0f);
                    break;
            }
            if (alreadySet)
            {
                return false;
            }

            bool written = false;
            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var behaviour in machine.behaviours.OfType<AnimatorDriver>())
                    {
                        foreach (var task in behaviour.EnterTasks.Concat(behaviour.ExitTasks))
                        {
                            written |= task.targetName == param.name;
                        }
                    }
                    foreach (var child in machine.states)
                    {
                        foreach (var behaviour in child.state.behaviours.OfType<AnimatorDriver>())
                        {
                            foreach (var task in behaviour.EnterTasks.Concat(behaviour.ExitTasks))
                            {
                                written |= task.targetName == param.name;
                            }
                        }
                    }
                });
            }
            if (written)
            {
                return false;
            }

            param.defaultBool = value > 0.5f;
            param.defaultInt = Mathf.RoundToInt(value);
            param.defaultFloat = value;
            return true;
        }

        /// <summary>
        /// Declares any parameter that transitions, blend trees or drivers still reference but
        /// the controller never defines.
        ///
        /// This is damage control, not a cure. Something upstream fails to carry certain
        /// declarations across on some avatars — a FinalIK quadruped arrived with a whole
        /// "Controls/Synced/*" family referenced by blend trees while not one of them was
        /// declared — and I have not found which pass drops them. What is certain is the cost of
        /// leaving it: ChilloutVR DROPS a transition whose condition names an unknown parameter,
        /// so a layer can stop advancing entirely, and Unity reports nothing.
        ///
        /// Declaring the missing name makes the controller self-consistent. A blend tree reading
        /// it gets 0, exactly as it did before, but transitions survive and behave predictably
        /// instead of silently disappearing. The parameters land as Float, which every reference
        /// kind can read, and every one is named in the report — if that list is ever long, or
        /// the same names recur across avatars, that is the trail to the real bug.
        /// </summary>
        static void DeclareDanglingParameters(AnimatorController master, BridgeContext ctx)
        {
            var declared = new HashSet<string>(master.parameters.Select(p => p.name));
            var missing = CollectReferencedParameters(master)
                .Where(n => !string.IsNullOrEmpty(n) && !declared.Contains(n))
                .OrderBy(n => n)
                .ToList();
            if (missing.Count == 0)
            {
                return;
            }

            var parameters = master.parameters.ToList();
            var mirrored = new List<string>();
            foreach (string name in missing)
            {
                float value = MirroredDefault(name, master, ctx);
                if (value != 0f)
                {
                    mirrored.Add($"{name} = {value:0.###}");
                }
                parameters.Add(new AnimatorControllerParameter
                {
                    name = name,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = value
                });
            }
            master.parameters = parameters.ToArray();

            if (mirrored.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"Took {mirrored.Count} missing parameter default(s) from the parameter each mirrors",
                    $"{string.Join(", ", mirrored.Take(10))}{(mirrored.Count > 10 ? ", …" : "")} — " +
                    "declaring these as 0 with the rest was actively wrong. A slider whose neutral " +
                    "is 0.5 sits at one end of its own range at 0, which is why limb and body " +
                    "sliders came out deformed rather than merely inert.");
            }

            ctx.Report.Warning(Category, $"Declared {missing.Count} referenced parameter(s) that were missing",
                $"{string.Join(", ", missing.Take(12))}{(missing.Count > 12 ? ", …" : "")} — transitions, blend " +
                "trees or drivers name these but nothing declared them. ChilloutVR drops a transition whose " +
                "condition names an unknown parameter, so they were added as Float 0 to keep those transitions " +
                "alive. Whatever drove them in VRChat still won't, so treat this as a repair, not a fix — if a " +
                "feature on this avatar is dead, start here.");
        }

        /// <summary>
        /// The default a dangling parameter should carry, taken from the parameter it mirrors.
        ///
        /// VRCFury copies a parameter it needs to read into its own namespace —
        /// "VF87_AvatarLimbScaling_Arms" alongside the real "AvatarLimbScaling_Arms" — and those
        /// copies are frequently the ones left undeclared. Declaring them 0 like any other
        /// dangling name is not neutral: Avatar Limb Scaling's sliders are 0.5 at rest, where 0
        /// means fully shrunk, and the body-shape sliders on the same avatar behave the same way.
        /// The result is an avatar that arrives visibly deformed rather than merely missing a
        /// feature — which is exactly how this was found.
        ///
        /// So: strip the "VF&lt;id&gt;_" tag, find whatever the copy shadows, and take its default.
        /// Falls back to 0, which is right for a genuinely unknown parameter.
        /// </summary>
        static float MirroredDefault(string name, AnimatorController master, BridgeContext ctx)
        {
            string bare = name.StartsWith("#", StringComparison.Ordinal) ? name.Substring(1) : name;
            var tag = System.Text.RegularExpressions.Regex.Match(bare, @"^VF\d+_(.+)$");
            if (!tag.Success)
            {
                return 0f;
            }
            string source = tag.Groups[1].Value;

            // The real parameter, however the rename pass ended up spelling it.
            foreach (var p in master.parameters)
            {
                if ((p.name == source || p.name == "#" + source) &&
                    p.type == AnimatorControllerParameterType.Float)
                {
                    return p.defaultFloat;
                }
            }

            // Otherwise what the avatar itself declared, which is where the 0.5 actually lives.
            var expressions = ctx.SourceDescriptor != null ? ctx.SourceDescriptor.expressionParameters : null;
            if (expressions != null && expressions.parameters != null)
            {
                foreach (var p in expressions.parameters)
                {
                    if (p != null && p.name == source)
                    {
                        return p.defaultValue;
                    }
                }
            }
            return 0f;
        }

        /// <summary>
        /// Points references at the "#"-prefixed parameter when the bare name they name doesn't
        /// exist and the prefixed one does.
        ///
        /// Non-synced parameters get a leading "#" (ChilloutVR's local-only convention). A
        /// reference left on the bare name after that is simply broken — Unity treats an unknown
        /// parameter as 0 and ChilloutVR drops the transition, so the feature silently stops
        /// working. Branwen showed "GestureLeftWeight" in ten blend trees while the declared,
        /// stream-fed parameter was "#GestureLeftWeight", used seven times as motion time: the
        /// same value, half the references renamed.
        ///
        /// This repairs the result rather than the cause. That's deliberate — the rewrite is
        /// only ever applied where the bare name is undeclared AND the prefixed one exists, which
        /// is exactly the broken shape and nothing else, so it holds regardless of which pass
        /// dropped the reference. Anything it changes is reported, so a pass that keeps needing
        /// this stays visible instead of being quietly papered over.
        /// </summary>
        static void RepairPrefixedReferences(AnimatorController master, BridgeContext ctx)
        {
            var declared = new HashSet<string>(master.parameters.Select(p => p.name));
            var fixes = new Dictionary<string, string>();
            foreach (string name in declared)
            {
                if (name.StartsWith("#") && !declared.Contains(name.Substring(1)))
                {
                    fixes[name.Substring(1)] = name;
                }
            }
            if (fixes.Count == 0)
            {
                return;
            }

            var repaired = new HashSet<string>();
            string Fix(string n)
            {
                if (n != null && fixes.TryGetValue(n, out var prefixed))
                {
                    repaired.Add(n);
                    return prefixed;
                }
                return n;
            }

            void FixMotion(Motion motion)
            {
                if (!(motion is BlendTree tree))
                {
                    return;
                }
                tree.blendParameter = Fix(tree.blendParameter);
                tree.blendParameterY = Fix(tree.blendParameterY);
                var children = tree.children;
                for (int i = 0; i < children.Length; i++)
                {
                    children[i].directBlendParameter = Fix(children[i].directBlendParameter);
                    FixMotion(children[i].motion);
                }
                tree.children = children;
            }

            void FixConditions(AnimatorTransitionBase[] transitions)
            {
                foreach (var transition in transitions)
                {
                    var conditions = transition.conditions;
                    bool changed = false;
                    for (int i = 0; i < conditions.Length; i++)
                    {
                        string fixedName = Fix(conditions[i].parameter);
                        if (fixedName != conditions[i].parameter)
                        {
                            conditions[i].parameter = fixedName;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        transition.conditions = conditions;
                    }
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    FixConditions(machine.anyStateTransitions);
                    FixConditions(machine.entryTransitions);
                    foreach (var behaviour in machine.behaviours)
                    {
                        RenameInDriver(behaviour as AnimatorDriver, Fix);
                    }
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        state.timeParameter = Fix(state.timeParameter);
                        state.speedParameter = Fix(state.speedParameter);
                        state.mirrorParameter = Fix(state.mirrorParameter);
                        state.cycleOffsetParameter = Fix(state.cycleOffsetParameter);
                        FixMotion(state.motion);
                        FixConditions(state.transitions);
                        foreach (var behaviour in state.behaviours)
                        {
                            RenameInDriver(behaviour as AnimatorDriver, Fix);
                        }
                    }
                });
            }

            if (repaired.Count > 0)
            {
                ctx.Report.Converted(Category, $"Repointed {repaired.Count} reference name(s) at their local parameter",
                    $"{string.Join(", ", repaired.OrderBy(r => r).Take(12))}{(repaired.Count > 12 ? ", …" : "")} — " +
                    "conditions, blend trees or drivers still named these without the \"#\" that marks a " +
                    "ChilloutVR local parameter, while only the prefixed version was declared. Unity reads an " +
                    "unknown parameter as 0, so those would have silently done nothing.");
            }
        }

        /// <summary>
        /// Removes parameters left declared but inert once the menu is settled.
        ///
        /// Kept only if something in the animator touches it, a menu entry drives it, a contact
        /// writes it, or ChilloutVR supplies it (core, gesture and stream-fed parameters).
        ///
        /// Deliberately does NOT honour ctx.PreserveParameters, unlike SystemStripper's earlier
        /// pass: that set exists to stop the RENAME pass altering synced names, which says
        /// nothing about whether a parameter should still exist. And a synced VRChat parameter
        /// doesn't carry its sync across anyway — in ChilloutVR a parameter syncs only via an
        /// Advanced Settings entry, so once that entry is gone the parameter is inert.
        /// </summary>
        static void PruneOrphanedParameters(AnimatorController master, BridgeContext ctx)
        {
            var referenced = CollectReferencedParameters(master);
            var menuNames = new HashSet<string>(ctx.CvrAvatar.avatarSettings.settings
                .Where(e => e != null && !string.IsNullOrEmpty(e.machineName))
                .Select(e => e.machineName));

            var parameters = master.parameters;
            var kept = parameters
                .Where(p => referenced.Contains(p.name) ||
                            menuNames.Contains(p.name) ||
                            IsGameDrivenParameter(p.name) ||
                            GestureMap.GestureParameters.Contains(p.name) ||
                            ctx.ContactParameters.Contains(p.name))
                .ToArray();

            int removed = parameters.Length - kept.Length;
            if (removed == 0)
            {
                return;
            }
            var names = parameters.Select(p => p.name).Except(kept.Select(p => p.name)).ToList();
            master.parameters = kept;
            ctx.Report.Converted(Category, $"Removed {removed} orphaned animator parameter(s)",
                $"{string.Join(", ", names.Take(12))}{(names.Count > 12 ? ", …" : "")} — nothing in the " +
                "animator reads them and their menu entries were removed as dead, so they were declared and " +
                "doing nothing. (They weren't costing sync either way: ChilloutVR syncs through Advanced " +
                "Settings entries, not animator parameters.)");
        }

        static HashSet<string> CollectReferencedParameters(AnimatorController master)
        {
            var referenced = new HashSet<string>();
            // Humanoid MUSCLE curves bind the same way an animated animator parameter does —
            // type Animator, empty path — so a locomotion clip otherwise contributes hundreds of
            // names like RootQ.x and LeftFootT.y. Requiring the name to be a declared parameter
            // separates them; a clip writing to an undeclared parameter does nothing anyway.
            var declaredNames = new HashSet<string>(master.parameters.Select(p => p.name));

            void NoteMotion(Motion motion)
            {
                if (motion is BlendTree tree)
                {
                    // Only the fields this blend type actually reads. blendParameter defaults to
                    // "Blend" on every tree Unity creates and survives as a leftover on Direct
                    // trees (VRCFury's are full of them), and blendParameterY is ignored by 1D
                    // trees. Counting the vestigial fields invented phantom references: every
                    // avatar with a Fury Direct tree "referenced" a parameter called Blend, and
                    // the scaler template "referenced" Smooth Amount and Value off a Direct
                    // tree's dead XY fields — which DeclareDanglingParameters then declared as
                    // Float 0 with a warning telling the user to investigate their avatar. The
                    // warning was AvatarBridge reporting its own reflection.
                    if (tree.blendType != BlendTreeType.Direct)
                    {
                        referenced.Add(tree.blendParameter);
                        if (tree.blendType != BlendTreeType.Simple1D)
                        {
                            referenced.Add(tree.blendParameterY);
                        }
                    }
                    foreach (var child in tree.children)
                    {
                        if (tree.blendType == BlendTreeType.Direct)
                        {
                            referenced.Add(child.directBlendParameter);
                        }
                        NoteMotion(child.motion);
                    }
                }
                else if (motion is AnimationClip clip)
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path)
                            && declaredNames.Contains(binding.propertyName))
                        {
                            referenced.Add(binding.propertyName);
                        }
                    }
                }
            }

            void NoteConditions(AnimatorTransitionBase[] transitions)
            {
                foreach (var transition in transitions)
                {
                    foreach (var condition in transition.conditions)
                    {
                        referenced.Add(condition.parameter);
                    }
                }
            }

            void NoteDrivers(IEnumerable<StateMachineBehaviour> behaviours)
            {
                foreach (var driver in behaviours.OfType<AnimatorDriver>())
                {
                    foreach (var task in driver.EnterTasks.Concat(driver.ExitTasks))
                    {
                        referenced.Add(task.targetName);
                        referenced.Add(task.aName);
                        referenced.Add(task.bName);
                    }
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    NoteConditions(machine.anyStateTransitions);
                    NoteConditions(machine.entryTransitions);
                    NoteDrivers(machine.behaviours);
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        if (state.timeParameterActive) referenced.Add(state.timeParameter);
                        if (state.speedParameterActive) referenced.Add(state.speedParameter);
                        if (state.mirrorParameterActive) referenced.Add(state.mirrorParameter);
                        if (state.cycleOffsetParameterActive) referenced.Add(state.cycleOffsetParameter);
                        NoteMotion(state.motion);
                        NoteConditions(state.transitions);
                        NoteDrivers(state.behaviours);
                    }
                });
            }
            return referenced;
        }

        /// <summary>
        /// Moves every comparison onto the compacted numbering.
        ///
        /// Exact matches follow the map. Boundaries move by counting instead: because the map
        /// is order-preserving, "&gt; t" still selects the same values if the threshold becomes
        /// the index of the last kept value that does NOT exceed t. Worked through on a real
        /// avatar — kept values [0,1,2,3,4,5,9,19,24,29], "&gt; 9" becomes "&gt; 6", which selects
        /// indices 7,8,9 = values 19,24,29: the same set as before.
        /// </summary>
        static void RemapIntConditions(AnimatorController master, string param,
            Dictionary<int, int> map, List<int> ordered)
        {
            void Remap(AnimatorTransitionBase[] transitions)
            {
                foreach (var transition in transitions)
                {
                    var conditions = transition.conditions;
                    bool changed = false;
                    for (int i = 0; i < conditions.Length; i++)
                    {
                        if (conditions[i].parameter != param)
                        {
                            continue;
                        }
                        int value = Mathf.RoundToInt(conditions[i].threshold);
                        int mapped;
                        switch (conditions[i].mode)
                        {
                            case AnimatorConditionMode.Greater:
                                mapped = ordered.Count(v => v <= value) - 1;
                                break;
                            case AnimatorConditionMode.Less:
                                mapped = ordered.Count(v => v < value);
                                break;
                            default:
                                if (!map.TryGetValue(value, out mapped))
                                {
                                    continue;
                                }
                                break;
                        }
                        if (mapped != value)
                        {
                            conditions[i].threshold = mapped;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        transition.conditions = conditions;
                    }
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    Remap(machine.anyStateTransitions);
                    Remap(machine.entryTransitions);
                    foreach (var child in machine.states)
                    {
                        Remap(child.state.transitions);
                    }
                });
            }
        }

        static void RemapIntDrivers(AnimatorController master, string param, Dictionary<int, int> map)
        {
            void RemapBehaviours(StateMachineBehaviour[] behaviours)
            {
                foreach (var behaviour in behaviours)
                {
                    if (!(behaviour is AnimatorDriver driver))
                    {
                        continue;
                    }
                    foreach (var task in driver.EnterTasks.Concat(driver.ExitTasks))
                    {
                        if (task.targetName == param &&
                            task.aType == AnimatorDriverTask.SourceType.Static &&
                            map.TryGetValue(Mathf.RoundToInt(task.aValue), out var mapped))
                        {
                            task.aValue = mapped;
                        }
                    }
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    RemapBehaviours(machine.behaviours);
                    foreach (var child in machine.states)
                    {
                        RemapBehaviours(child.state.behaviours);
                    }
                });
            }
        }

        /// <summary>Contact triggers that set this parameter must follow the renumbering too.</summary>
        static void RemapTriggerValues(BridgeContext ctx, string param, Dictionary<int, int> map)
        {
            foreach (var trigger in ctx.CvrAvatar.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true))
            {
                foreach (var task in trigger.enterTasks.Concat(trigger.exitTasks))
                {
                    if (task.settingName == param &&
                        map.TryGetValue(Mathf.RoundToInt(task.settingValue), out var mapped))
                    {
                        task.settingValue = mapped;
                    }
                }
                EditorUtility.SetDirty(trigger);
            }
        }

        /// <summary>
        /// CVR Parameter Streams feed values VRChat provided as built-in parameters:
        /// trigger squeeze (GestureLeft/RightWeight), mute state and VR mode. Entries are
        /// created via reflection because enum member layouts vary between CCK versions.
        /// </summary>
        static void CreateParameterStreams(AnimatorController master, BridgeContext ctx)
        {
            // "app"/"lo"/"hi" describe how the raw stream value reaches the parameter. Override
            // passes it through; Remap runs the CCK's Remap(value, 0, 1, lo, hi), which is how a
            // ChilloutVR boolean becomes one of VRChat's numbered states.
            var streamables = new[]
            {
                (bare: "GestureLeftWeight", streamType: "TriggerLeftValue", app: "Override", lo: 0f, hi: 0f),
                (bare: "GestureRightWeight", streamType: "TriggerRightValue", app: "Override", lo: 0f, hi: 0f),
                (bare: "MuteSelf", streamType: "LocalPlayerMuted", app: "Override", lo: 0f, hi: 0f),
                (bare: "VRMode", streamType: "DeviceMode", app: "Override", lo: 0f, hi: 0f),

                // AvatarUpright is Clamp01(currentHeight / avatarHeight) and rests at 1, which is
                // VRChat's Upright exactly — same range, same meaning, no conversion needed.
                (bare: "Upright", streamType: "AvatarUpright", app: "Override", lo: 0f, hi: 0f),

                // ChilloutVR only reports whether full-body tracking is on, so the six VRChat
                // states collapse to the two that matter: 3 = head and hands, 6 = full body.
                // Both are above zero, which is what most avatars actually test for.
                //
                // (VelocityX/Y/Z need no stream and must never get one: they are CLIENT CORE
                // parameters — BetterBetterCharacterController feeds the local player, and
                // PuppetMaster feeds every remote copy from its root velocity — and core
                // parameters are read-only to streams anyway. VelocityMagnitude is different:
                // the client does not compute it, so FeedVelocityMagnitude derives it in the
                // animator from the native three.)
                (bare: "TrackingType", streamType: "LocalPlayerFullBodyEnabled", app: "Remap", lo: 3f, hi: 6f)
            };
            var wanted = new List<(string paramName, string streamType, string bare, string app, float lo, float hi)>();
            foreach (var param in master.parameters)
            {
                string bare = param.name.TrimStart('#');
                foreach (var s in streamables)
                {
                    if (bare == s.bare)
                    {
                        wanted.Add((param.name, s.streamType, s.bare, s.app, s.lo, s.hi));
                    }
                }
            }
            if (wanted.Count == 0)
            {
                return;
            }

            var entryType = typeof(CVRParameterStream).Assembly.GetType("ABI.CCK.Components.CVRParameterStreamEntry");
            var typeEnum = entryType?.GetNestedType("Type");
            var targetEnum = entryType?.GetNestedType("TargetType");
            var appEnum = entryType?.GetNestedType("ApplicationType");
            if (entryType == null || typeEnum == null)
            {
                ctx.Report.Warning(Category, "CVRParameterStream entries unavailable on this CCK version",
                    "GestureLeftWeight/MuteSelf/VRMode parameters keep their defaults.");
                return;
            }

            // The serialized field is an int; the CLIENT is what reads it back. When the
            // installed CCK's enum predates a name, writing the client's numeric value still
            // round-trips perfectly — the decompiled client's numbers are the contract
            // (ApplicationType: Override=0, Remap=201, ClampRemap=202; Type: DeviceMode=20,
            // LocalPlayerMuted=210, LocalPlayerFullBodyEnabled=260, TriggerLeftValue=270,
            // TriggerRightValue=280, AvatarUpright=401). Before this, a CCK without "Remap"
            // silently cost the TrackingType stream.
            var clientEnumValues = new Dictionary<string, int>
            {
                { "Override", 0 }, { "Remap", 201 }, { "ClampRemap", 202 },
                { "DeviceMode", 20 }, { "LocalPlayerMuted", 210 },
                { "LocalPlayerFullBodyEnabled", 260 }, { "TriggerLeftValue", 270 },
                { "TriggerRightValue", 280 }, { "AvatarUpright", 401 },
            };
            object ParseEnum(Type enumType, string name)
            {
                if (enumType == null)
                {
                    return null;
                }
                try { return Enum.Parse(enumType, name, true); }
                catch
                {
                    if (clientEnumValues.TryGetValue(name, out int clientValue))
                    {
                        try { return Enum.ToObject(enumType, clientValue); } catch { }
                    }
                    return null;
                }
            }
            void SetField(object target, string fieldName, object value)
            {
                var field = entryType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                if (field != null && value != null)
                {
                    try { field.SetValue(target, value); } catch { }
                }
            }

            var stream = ctx.Target.GetComponent<CVRParameterStream>();
            if (stream == null)
            {
                stream = ctx.Target.AddComponent<CVRParameterStream>();
            }
            var entriesField = stream.GetType().GetField("entries", BindingFlags.Public | BindingFlags.Instance);
            var entries = entriesField?.GetValue(stream) as System.Collections.IList;
            if (entries == null && entriesField != null)
            {
                entries = (System.Collections.IList)Activator.CreateInstance(entriesField.FieldType);
                entriesField.SetValue(stream, entries);
            }
            if (entries == null)
            {
                ctx.Report.Warning(Category, "CVRParameterStream has no entries list on this CCK version",
                    "Stream-fed parameters keep their defaults.");
                return;
            }

            foreach (var w in wanted)
            {
                var streamTypeValue = ParseEnum(typeEnum, w.streamType);
                if (streamTypeValue == null)
                {
                    ctx.Report.Skipped(Category, $"Parameter stream {w.streamType}",
                        $"Stream type not found on this CCK version; \"{w.paramName}\" keeps its default.");
                    continue;
                }
                var appValue = ParseEnum(appEnum, w.app);
                if (appValue == null)
                {
                    // Without the arithmetic this entry would feed a raw 0/1 into a parameter
                    // that means something else entirely. Leaving the default alone is safer.
                    ctx.Report.Skipped(Category, $"Parameter stream {w.streamType}",
                        $"This CCK version has no \"{w.app}\" application type; \"{w.paramName}\" keeps its default.");
                    continue;
                }
                var entry = Activator.CreateInstance(entryType);
                SetField(entry, "type", streamTypeValue);
                if (w.app == "Remap")
                {
                    SetField(entry, "staticValue", w.lo);
                    SetField(entry, "staticValue2", w.hi);
                }
                // TargetType.Animator is the CCK's "Sub Animator" — an Animator on some target
                // GameObject you nominate. Left as that, every entry sat with an empty target and
                // the inspector's "Target object does not have an Animator component!" warning, so
                // nothing was ever fed. AvatarAnimator is the avatar's own animator, which is what
                // these parameters need; fall back to the old value if a CCK version lacks it.
                SetField(entry, "targetType",
                    ParseEnum(targetEnum, "AvatarAnimator") ?? ParseEnum(targetEnum, "Animator"));
                SetField(entry, "applicationType", appValue);
                SetField(entry, "parameterName", w.paramName);
                entries.Add(entry);
                ctx.Report.Converted(Category, $"\"{w.paramName}\" fed by CVR Parameter Stream ({w.streamType})",
                    (w.app == "Remap"
                        ? $"Behaves like the VRChat built-in parameter, remapped to {w.lo:0.##}–{w.hi:0.##}. "
                        : "Behaves like the VRChat built-in parameter. ") +
                    "Kept synced rather than local: the stream runs only on the wearer's copy of the avatar, " +
                    "so anything this drives would otherwise sit frozen at its default for everyone else.");
            }
            EditorUtility.SetDirty(stream);
        }

        /// <summary>
        /// VRChat's VelocityMagnitude has no ChilloutVR source, but its three components do:
        /// VelocityX/Y/Z are CLIENT CORE parameters, fed for the local player by
        /// BetterBetterCharacterController and for every remote copy by PuppetMaster (both
        /// decompiled). So the magnitude is derived inside the animator: a minimal
        /// self-looping state whose AnimatorDriver recomputes sqrt(x² + y² + z²) every cycle.
        ///
        /// The parameter keeps its "#" local prefix deliberately: every client runs the
        /// animator, so every client computes the value for every copy — exactly how the
        /// VRChat built-in behaved, at zero sync cost. GoGo Loco gates much of its locomotion
        /// on this parameter; frozen at 0 it read as "never moving" and a kept GoGo install
        /// sat half-dead.
        /// </summary>
        static void FeedVelocityMagnitude(AnimatorController master, BridgeContext ctx)
        {
            AnimatorControllerParameter target = null;
            foreach (var p in master.parameters)
            {
                if (p.name.TrimStart('#') == "VelocityMagnitude")
                {
                    target = p;
                    break;
                }
            }
            if (target == null)
            {
                return;
            }

            // The driver reads the native components and one scratch cell; declare what the
            // avatar didn't. The natives are Floats the client feeds by exact name.
            var parameters = master.parameters.ToList();
            void Ensure(string name)
            {
                if (parameters.All(p => p.name != name))
                {
                    parameters.Add(new AnimatorControllerParameter
                    {
                        name = name,
                        type = AnimatorControllerParameterType.Float,
                        defaultFloat = 0f
                    });
                }
            }
            const string Scratch = "#VelocityMagnitudeCalc";
            Ensure("VelocityX");
            Ensure("VelocityY");
            Ensure("VelocityZ");
            Ensure(Scratch);
            master.parameters = parameters.ToArray();

            AnimatorDriverTask Task(string targetName, AnimatorDriverTask.Operator op,
                string aParam, string bParam, float bStatic = 0f)
            {
                var task = new AnimatorDriverTask
                {
                    targetType = AnimatorDriverTask.ParameterType.Float,
                    targetName = targetName,
                    op = op,
                    aType = AnimatorDriverTask.SourceType.Parameter,
                    aParamType = AnimatorDriverTask.ParameterType.Float,
                    aName = aParam
                };
                if (bParam != null)
                {
                    task.bType = AnimatorDriverTask.SourceType.Parameter;
                    task.bParamType = AnimatorDriverTask.ParameterType.Float;
                    task.bName = bParam;
                }
                else
                {
                    task.bType = AnimatorDriverTask.SourceType.Static;
                    task.bValue = bStatic;
                }
                return task;
            }

            // A 1/60 s clip whose only curve targets an undeclared animator parameter — it
            // exists to give the state a length, so the exit-time self-transition below cycles
            // and the enter tasks re-run every cycle.
            var tick = new AnimationClip { name = "VelocityMagnitude Tick" };
            tick.SetCurve("", typeof(Animator), Scratch + "Tick", AnimationCurve.Constant(0f, 1f / 60f, 0f));

            var machine = new AnimatorStateMachine
            {
                name = "VelocityMagnitude Feed",
                hideFlags = HideFlags.HideInHierarchy
            };
            var state = machine.AddState("Recompute");
            state.writeDefaultValues = false;
            state.motion = tick;
            machine.defaultState = state;

            var loop = state.AddTransition(state);
            loop.hasExitTime = true;
            loop.exitTime = 1f;
            loop.hasFixedDuration = true;
            loop.duration = 0f;

            var driver = state.AddStateMachineBehaviour<AnimatorDriver>();
            driver.localOnly = false; // remote copies must compute too — their VelocityX/Y/Z are fed
            driver.EnterTasks.Add(Task(Scratch, AnimatorDriverTask.Operator.Multiplication, "VelocityX", "VelocityX"));
            driver.EnterTasks.Add(Task(target.name, AnimatorDriverTask.Operator.Multiplication, "VelocityY", "VelocityY"));
            driver.EnterTasks.Add(Task(Scratch, AnimatorDriverTask.Operator.Addition, Scratch, target.name));
            driver.EnterTasks.Add(Task(target.name, AnimatorDriverTask.Operator.Multiplication, "VelocityZ", "VelocityZ"));
            driver.EnterTasks.Add(Task(Scratch, AnimatorDriverTask.Operator.Addition, Scratch, target.name));
            driver.EnterTasks.Add(Task(target.name, AnimatorDriverTask.Operator.Power, Scratch, null, 0.5f));

            var layers = master.layers.ToList();
            layers.Add(new AnimatorControllerLayer
            {
                name = "VelocityMagnitude Feed",
                defaultWeight = 1f,
                stateMachine = machine
            });
            master.layers = layers.ToArray();

            ctx.Report.Converted(Category, $"\"{target.name}\" computed from the native velocity",
                "VRChat's VelocityMagnitude has no ChilloutVR source, but VelocityX/Y/Z are client " +
                "core parameters — fed for the local player and for every remote copy alike — so a " +
                "generated driver layer recomputes sqrt(x²+y²+z²) each cycle. Kept local (\"#\"): " +
                "every client computes it for every copy, which is how the VRChat built-in behaved, " +
                "at zero sync cost. Locomotion systems like GoGo Loco gate on this; frozen at 0 it " +
                "read as never-moving.");
        }

        internal static string SanitizeParameterName(string source)
        {
            var parts = System.Text.RegularExpressions.Regex
                .Split(source ?? "", "[^A-Za-z0-9]+")
                .Where(p => p.Length > 0)
                .Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1));
            string result = string.Concat(parts);
            if (string.IsNullOrEmpty(result))
            {
                result = "Param";
            }
            if (char.IsDigit(result[0]))
            {
                result = "P" + result;
            }
            return result;
        }

        /// <summary>
        /// Copies an asset a clip points at out of Fury's temp and into the output, returning the
        /// copy — or the original when it was never volatile.
        ///
        /// Two cases, and getting them confused copies the wrong thing entirely.
        ///
        /// A standalone file is copied with CopyAsset, which preserves import settings and works
        /// for any type. But VRCFury also embeds generated materials *inside* its controllers as
        /// sub-assets, and GetAssetPath on a sub-asset returns the containing file — so CopyAsset
        /// duplicates a whole animator controller into the output, and loading a Material back out
        /// of that path returns null because the main asset is a controller. The reference is then
        /// left pointing at the doomed original: exactly the failure this was written to fix,
        /// wearing a disguise. Sub-assets are therefore cloned as objects instead.
        /// </summary>
        static UnityEngine.Object RehomeReferencedAsset(UnityEngine.Object value, BridgeContext ctx,
            Dictionary<UnityEngine.Object, UnityEngine.Object> done)
        {
            if (value == null)
            {
                return value;
            }
            if (done.TryGetValue(value, out var already))
            {
                return already;
            }

            string source = AssetDatabase.GetAssetPath(value);
            if (string.IsNullOrEmpty(source) || !IsDoomedGeneratedPath(source))
            {
                done[value] = value;
                return value; // a permanent project asset — leave it where it is
            }

            string dir = ctx.OutputDir.TrimEnd('/') + "/RehomedAssets";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder(ctx.OutputDir.TrimEnd('/'), "RehomedAssets");
            }
            UnityEngine.Object copy = value;
            if (AssetDatabase.IsMainAsset(value))
            {
                string target = AssetDatabase.GenerateUniqueAssetPath(
                    dir + "/" + System.IO.Path.GetFileName(source));
                if (AssetDatabase.CopyAsset(source, target))
                {
                    AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
                    copy = AssetDatabase.LoadAssetAtPath(target, value.GetType()) ?? value;
                }
            }
            else
            {
                // Embedded in something else — clone the object itself, or we'd drag its whole
                // container along and still not rescue the thing that was actually referenced.
                var clone = UnityEngine.Object.Instantiate(value);
                clone.name = value.name;
                string extension = value is Material ? ".mat"
                                 : value is AnimationClip ? ".anim"
                                 : ".asset";
                string target = AssetDatabase.GenerateUniqueAssetPath(
                    dir + "/" + SanitizeFileName(value.name) + extension);
                AssetDatabase.CreateAsset(clone, target);
                copy = clone;
            }

            done[value] = copy;

            // A material is not a leaf. VRCFury repacks textures into its own container assets in
            // temp, so a rescued material can still point every texture slot at something about to
            // be deleted — which doesn't render magenta like a missing material, it renders as an
            // untextured wash. On "Kaides Expie" that was a white face with no eyes, from a
            // material that had copied across perfectly.
            if (copy is Material material)
            {
                RehomeMaterialContents(material, ctx, done);
            }

            if (copy != value)
            {
                ctx.Report.Converted("Assets", $"Re-homed \"{value.name}\" out of temp",
                    "An animation clip assigns this, which is a reference the clip copy alone " +
                    "doesn't rescue — VRCFury's next build would delete it and the clip would " +
                    "assign nothing.");
            }
            return copy;
        }

        /// <summary>
        /// Rescues the shader and textures a re-homed material depends on, so the material is
        /// still whole once VRCFury clears its temp folder.
        /// </summary>
        static void RehomeMaterialContents(Material material, BridgeContext ctx,
            Dictionary<UnityEngine.Object, UnityEngine.Object> done)
        {
            var shader = material.shader;
            if (shader == null)
            {
                return;
            }
            // Shader first: assigning one can drop properties the new shader lacks, and doing it
            // after the textures would undo them.
            if (RehomeReferencedAsset(shader, ctx, done) is Shader rescuedShader && rescuedShader != shader)
            {
                material.shader = rescuedShader;
                shader = rescuedShader;
            }

            bool changed = false;
            int count = UnityEditor.ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                if (UnityEditor.ShaderUtil.GetPropertyType(shader, i)
                    != UnityEditor.ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    continue;
                }
                string property = UnityEditor.ShaderUtil.GetPropertyName(shader, i);
                var texture = material.GetTexture(property);
                if (texture == null)
                {
                    continue;
                }
                if (RehomeReferencedAsset(texture, ctx, done) is Texture rescued && rescued != texture)
                {
                    material.SetTexture(property, rescued);
                    changed = true;
                }
            }
            if (changed)
            {
                EditorUtility.SetDirty(material);
            }
        }

        /// <summary>
        /// VRCFury bakes its generated clips and masks into Packages/com.vrcfury.temp,
        /// which Fury DELETES on the next build — leaving every reference as "None".
        /// Copy anything volatile into our own controller so the output is self-contained.
        ///
        /// That covers the clips themselves and, since a clip's object-reference curves point at
        /// assets living in the same doomed folder, whatever those curves assign.
        /// </summary>
        static void RehomeVolatileAssets(AnimatorController master, List<AnimatorControllerLayer> vrcLayers, BridgeContext ctx)
        {
            var clipMap = new Dictionary<AnimationClip, AnimationClip>();
            var volatileAssets = new Dictionary<UnityEngine.Object, UnityEngine.Object>();

            bool IsVolatile(UnityEngine.Object obj)
            {
                if (obj == null)
                {
                    return false;
                }
                string path = AssetDatabase.GetAssetPath(obj);
                // No asset path = an in-memory object. A Fury bake that errors partway (its
                // ErrorDialogBoundary swallows the exception and carries on) leaves its generated
                // clips unsaved and flagged DontSave — Unity's persistence then REFUSES them at
                // save time ("kDontSaveInEditor" assertions) and the controller keeps dangling
                // references: "Missing (Motion)" in every affected tree, 61 gutted states on the
                // avatar that found this. Anything not an asset must be cloned to survive saving.
                if (string.IsNullOrEmpty(path))
                {
                    return true;
                }
                if ((obj.hideFlags & HideFlags.DontSave) != 0)
                {
                    return true;
                }
                return IsDoomedGeneratedPath(path);
            }

            AnimationClip RehomeClip(AnimationClip clip)
            {
                if (clip == null || !IsVolatile(clip))
                {
                    return clip;
                }
                if (!clipMap.TryGetValue(clip, out var clone))
                {
                    clone = UnityEngine.Object.Instantiate(clip);
                    clone.name = clip.name;
                    // The whole point of the clone is that it CAN persist; a copied DontSave flag
                    // would recreate exactly the amputation this exists to prevent.
                    clone.hideFlags = HideFlags.None;
                    clipMap[clip] = clone;
                    RehomeClipReferences(clone);
                }
                return clone;
            }

            // Copying the clip is only half of it. A clip that swaps a material carries that
            // material as an object-reference curve, and cloning the clip copies the *reference*,
            // which still points into Fury's temp. The clip then survives the next Fury build and
            // the material it assigns does not — so the toggle works right up until it is used,
            // and the mesh turns magenta. Found on "Kaides Expie": a "Milky" toggle that had
            // converted cleanly weeks running, then broke with nothing about it having changed.
            void RehomeClipReferences(AnimationClip clone)
            {
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clone))
                {
                    var keys = AnimationUtility.GetObjectReferenceCurve(clone, binding);
                    if (keys == null)
                    {
                        continue;
                    }
                    bool changed = false;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        var rescued = RehomeReferencedAsset(keys[i].value, ctx, volatileAssets);
                        if (rescued != keys[i].value)
                        {
                            keys[i].value = rescued;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        AnimationUtility.SetObjectReferenceCurve(clone, binding, keys);
                    }
                }
            }

            var treeMap = new Dictionary<BlendTree, BlendTree>();

            Motion RehomeMotion(Motion motion)
            {
                if (motion is AnimationClip clip)
                {
                    return RehomeClip(clip);
                }
                if (motion is BlendTree tree)
                {
                    // A volatile tree must be CLONED, not repaired in place. The old code rescued
                    // the children and then handed back the SAME temp tree object — so the
                    // controller kept referencing an asset inside Packages/com.vrcfury.temp, and
                    // Fury's next build (which play mode triggers on the original avatar still in
                    // the scene) wiped it. One avatar lost 283 motion references exactly this way:
                    // worked on the first play, gutted on the second.
                    if (IsVolatile(tree))
                    {
                        if (!treeMap.TryGetValue(tree, out var cloneTree))
                        {
                            cloneTree = new BlendTree
                            {
                                name = tree.name,
                                blendType = tree.blendType,
                                blendParameter = tree.blendParameter,
                                blendParameterY = tree.blendParameterY,
                                hideFlags = HideFlags.HideInHierarchy
                            };
                            treeMap[tree] = cloneTree;
                            // Same threshold discipline as AnimatorDeepCopier: Unity clamps child
                            // thresholds into [min,max] the moment those are assigned, and manual
                            // trees ship min = max = 0 — assigning them first crushes every
                            // threshold. Only automatic trees get min/max, and automatic is
                            // restored after the children are in.
                            cloneTree.useAutomaticThresholds = false;
                            if (tree.useAutomaticThresholds)
                            {
                                cloneTree.minThreshold = tree.minThreshold;
                                cloneTree.maxThreshold = tree.maxThreshold;
                            }
                            var kids = tree.children;
                            for (int i = 0; i < kids.Length; i++)
                            {
                                kids[i].motion = RehomeMotion(kids[i].motion);
                            }
                            cloneTree.children = kids;
                            if (tree.useAutomaticThresholds)
                            {
                                cloneTree.useAutomaticThresholds = true;
                            }
                        }
                        return cloneTree;
                    }

                    var children = tree.children;
                    for (int i = 0; i < children.Length; i++)
                    {
                        children[i].motion = RehomeMotion(children[i].motion);
                    }
                    tree.children = children;
                }
                return motion;
            }

            // Every layer, not just the merged VRChat ones: any layer the pipeline has created by
            // this point can reference a Fury temp motion, and a reference this walk doesn't see
            // is a reference the temp wipe kills.
            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        child.state.motion = RehomeMotion(child.state.motion);
                    }
                });
            }

            var layers = master.layers;
            int rehomedMasks = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                var mask = layers[i].avatarMask;
                if (mask != null && IsVolatile(mask))
                {
                    var clone = UnityEngine.Object.Instantiate(mask);
                    clone.name = mask.name;
                    layers[i].avatarMask = clone;
                    rehomedMasks++;
                }
            }
            if (rehomedMasks > 0)
            {
                master.layers = layers;
            }

            if (clipMap.Count > 0 || rehomedMasks > 0)
            {
                ctx.Report.Converted(Category,
                    $"Re-homed {clipMap.Count} clip(s) and {rehomedMasks} mask(s) out of VRCFury's temp assets",
                    "Fury deletes Packages/com.vrcfury.temp on its next build; without copies, motions would turn to 'None'.");
            }
        }

        /// <summary>
        /// VRCFury bakes bool menu parameters as FLOAT animator parameters. CVR writes
        /// menu values using the entry's declared type — writing Bool into a Float
        /// animator parameter silently does nothing, which kills every toggle. Align
        /// each menu entry's type with the actual animator parameter type.
        /// </summary>
        static void ReconcileAasInputTypes(AnimatorController master, BridgeContext ctx)
        {
            var types = new Dictionary<string, AnimatorControllerParameterType>();
            foreach (var param in master.parameters)
            {
                types[param.name] = param.type;
            }

            int retyped = 0;
            foreach (var entry in ctx.CvrAvatar.avatarSettings.settings)
            {
                if (entry.setting == null || string.IsNullOrEmpty(entry.machineName))
                {
                    continue;
                }
                if (!types.TryGetValue(entry.machineName, out var animatorType))
                {
                    continue;
                }
                ABI.CCK.Scripts.CVRAdvancesAvatarSettingBase.ParameterType desired;
                switch (animatorType)
                {
                    case AnimatorControllerParameterType.Int:
                        desired = ABI.CCK.Scripts.CVRAdvancesAvatarSettingBase.ParameterType.Int;
                        break;
                    case AnimatorControllerParameterType.Bool:
                        desired = ABI.CCK.Scripts.CVRAdvancesAvatarSettingBase.ParameterType.Bool;
                        break;
                    default:
                        desired = ABI.CCK.Scripts.CVRAdvancesAvatarSettingBase.ParameterType.Float;
                        break;
                }
                if (entry.setting.usedType != desired)
                {
                    entry.setting.usedType = desired;
                    retyped++;
                }
            }
            if (retyped > 0)
            {
                EditorUtility.SetDirty(ctx.CvrAvatar);
                ctx.Report.Converted(Category, $"{retyped} menu entr(ies) retyped to match animator parameters",
                    "Prevents dead toggles when VRCFury bakes bool parameters as floats.");
            }
        }

        /// <summary>
        /// FX-sourced layers that animate body muscles or transforms fight ChilloutVR's
        /// locomotion. Flag them so the user knows exactly which layer is responsible.
        /// </summary>
        static readonly HashSet<string> MuscleCurveNames = new HashSet<string>(HumanTrait.MuscleName.Select(name =>
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^(Left|Right) (Thumb|Index|Middle|Ring|Little) (.*)$");
            return match.Success ? $"{match.Groups[1].Value}Hand.{match.Groups[2].Value}.{match.Groups[3].Value}" : name;
        }));

        static bool IsFingerCurve(string property) => property.Contains("Hand.");

        static bool IsRootCurve(string property) =>
            property.StartsWith("RootT") || property.StartsWith("RootQ") ||
            property.StartsWith("MotionT") || property.StartsWith("MotionQ");

        /// <summary>What a layer's clips actually touch on the humanoid rig.</summary>
        static void InspectLayerCurves(AnimatorControllerLayer layer, out bool body, out bool fingers)
        {
            bool foundBody = false, foundFingers = false;
            WalkMachines(layer.stateMachine, machine =>
            {
                foreach (var child in machine.states)
                {
                    foreach (var clip in CollectClips(child.state.motion))
                    {
                        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        {
                            if (binding.type != typeof(Animator) || !string.IsNullOrEmpty(binding.path))
                            {
                                continue;
                            }
                            if (IsRootCurve(binding.propertyName))
                            {
                                foundBody = true;
                            }
                            else if (MuscleCurveNames.Contains(binding.propertyName))
                            {
                                if (IsFingerCurve(binding.propertyName)) { foundFingers = true; } else { foundBody = true; }
                            }
                        }
                    }
                }
            });
            body = foundBody;
            fingers = foundFingers;
        }

        /// <summary>
        /// Stops merged VRChat layers writing over ChilloutVR's locomotion.
        ///
        /// In VRChat, FX is its own playable layer and simply cannot drive humanoid muscles.
        /// Everything here ends up in one controller instead, where that protection doesn't
        /// exist — and a state with Write Defaults on writes default values for every property
        /// animated anywhere in that controller, which now includes the muscles ChilloutVR's own
        /// locomotion clips animate. An unmasked FX layer sitting above Locomotion at weight 1
        /// therefore re-asserts the rest pose every frame and fights it. One avatar arrived with
        /// 40 layers, 35 of them unmasked, 128 states and Write Defaults on in every one, its
        /// legs cycling as though pedalling while it stood still.
        ///
        /// An avatar mask restores the separation: humanoid parts off means the layer cannot
        /// touch muscles, while an empty transform list leaves object toggles, blendshapes and
        /// material animation exactly as they were.
        ///
        /// Applied only to layers whose clips animate no muscles themselves, so it can never
        /// remove animation the avatar intended. Layers that do animate fingers get a hands mask
        /// instead of losing them, and layers that genuinely animate the body are left alone for
        /// WarnLocomotionOverrides to report.
        /// </summary>
        /// <summary>
        /// Names merged layers that can write humanoid muscles while carrying no mask to stop them.
        ///
        /// Reported rather than fixed, because masking is not always right: a layer may animate the
        /// body on purpose, and the pass that does the masking skips exactly those. This only says
        /// what it found and which switch addresses it.
        /// </summary>
        static void ReportUnmaskedMuscleLayers(AnimatorController master,
            List<AnimatorControllerLayer> vrcLayers, BridgeContext ctx)
        {
            var vrcNames = new HashSet<string>(vrcLayers.Select(l => l.name));
            var suspects = new List<string>();
            foreach (var layer in master.layers)
            {
                if (!vrcNames.Contains(layer.name) || layer.avatarMask != null)
                {
                    continue;
                }
                InspectLayerCurves(layer, out bool body, out bool fingers);
                // Everything the masking pass would act on — which is every merged layer that
                // does not deliberately animate the body. This used to read `!body && fingers`,
                // which named only the finger-animating layers: on the avatar that confirmed the
                // bicycle-pose fix, 18 of the 20 layers masking repaired were finger-free, so the
                // report was silent about the bulk of the problem and would have said nothing at
                // all on an avatar with no finger layers — precisely the avatars that need the
                // switch pointed out.
                if (!body)
                {
                    suspects.Add(layer.name);
                }
            }
            if (suspects.Count == 0)
            {
                return;
            }
            ctx.Report.Approximated(Category,
                $"{suspects.Count} merged layer(s) can write humanoid muscles with no mask",
                $"{string.Join(", ", suspects.Take(6))}{(suspects.Count > 6 ? ", …" : "")} — VRChat keeps " +
                "FX on its own playable layer, which stops this happening there; ChilloutVR runs one " +
                "controller, so nothing stops it here. If the avatar stands in a bent rest pose in game " +
                "with only the head and hands following you, turn on \"Mask merged layers off the " +
                "humanoid rig\" in Advanced and convert again. Layers that animate the body deliberately " +
                "are left alone by that option, so it is safe to try.");
        }

        static void MaskMergedLayers(AnimatorController master, List<AnimatorControllerLayer> vrcLayers, BridgeContext ctx)
        {
            var vrcNames = new HashSet<string>(vrcLayers.Select(l => l.name));
            var layers = master.layers;
            int masked = 0, handed = 0;

            foreach (var layer in layers)
            {
                if (!vrcNames.Contains(layer.name) || layer.avatarMask != null)
                {
                    continue;
                }
                InspectLayerCurves(layer, out bool body, out bool fingers);
                if (body)
                {
                    continue; // deliberate body animation; reported separately
                }
                // Finger curves get NO special treatment — they are blocked with the rest.
                //
                // Narrowing such a layer to a hands-only mask looked generous: keep whatever
                // finger animation the FX layer had. It is actually the opposite. The premise of
                // this whole pass is that VRChat's FX playable layer CANNOT drive humanoid
                // muscles — so those finger curves never moved a finger in VRChat either. A
                // hands mask hands them a power VRChat denied them, and every merged FX layer
                // sits ABOVE the CCK's LeftHand/RightHand layers in the stack, so on Override at
                // weight 1 they overwrite the hand pose the moment it plays.
                //
                // That is exactly how a converted avatar ends up with the CCK Debugger reporting
                // "LeftHand — weight 1.00, playing Thumbs Up 1.00" while the fingers sit in their
                // rest pose: two material-swap layers, of all things, were masked to fingers-only
                // and stomping the gesture every frame. Gestures matter; a dead finger curve on a
                // material swap does not.
                layer.avatarMask = GetNoMuscleMask(ctx);
                masked++;
                if (fingers)
                {
                    handed++;
                }
            }

            if (masked == 0)
            {
                return;
            }
            master.layers = layers;
            ctx.Report.Converted(Category, $"{masked} merged layer(s) masked off the humanoid rig",
                "VRChat's FX layer cannot drive humanoid muscles; merged into one ChilloutVR controller it " +
                "could, and any state with Write Defaults on would then re-assert the rest pose over " +
                "locomotion every frame. Object toggles, blendshapes and material animation are unaffected." +
                (handed > 0
                    ? $" {handed} of them carried finger curves, which are blocked too — they could not move a " +
                      "finger in VRChat either, and letting them through here would overwrite your hand " +
                      "gestures, since merged layers sit above the hand-pose layers."
                    : ""));
        }

        /// <summary>
        /// The last line of defence for hand gestures, and the reason this exists is worth
        /// writing down: a tester spent five rounds on an avatar whose CCK Debugger read
        /// "LeftHand — Layer Weight 1.00, playing Thumbs Up 1.00" while the fingers sat in
        /// their rest pose. The animator was right every time it was checked. Two MATERIAL
        /// SWAP layers above it carried a fingers-only mask, and on Override at weight 1 they
        /// rewrote the finger muscles every frame — with no finger curves of their own, purely
        /// by writing defaults into channels their mask let through.
        ///
        /// The rule is exact. When the CCK's own LeftHand/RightHand layers survive — which is
        /// whenever the avatar's Gesture layer was not converted — posing the fingers is THEIR
        /// job, and any layer above them that may write finger muscles is damage. MaskMergedLayers
        /// covers the case it owns, merged layers that arrive with NO mask; a layer that arrives
        /// WITH one is skipped by it, and injected layers never pass through it at all. So this
        /// checks the finished stack rather than trusting the passes that built it.
        /// </summary>
        static void AuditHandPoseConflicts(AnimatorController master, bool convertingGestureLayer, BridgeContext ctx)
        {
            if (convertingGestureLayer)
            {
                return; // the avatar's own gesture layers ARE the hand pose — nothing to protect
            }
            var layers = master.layers;
            int handTop = -1;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].name == "LeftHand" || layers[i].name == "RightHand")
                {
                    handTop = i;
                }
            }
            if (handTop < 0)
            {
                return;
            }

            var repaired = new List<string>();
            var warned = new List<string>();
            bool changed = false;
            for (int i = handTop + 1; i < layers.Length; i++)
            {
                var layer = layers[i];
                var mask = layer.avatarMask;
                // A null mask is MaskMergedLayers' business, not this one's — flagging those
                // here would fire on every injected layer we write ourselves.
                if (mask == null || layer.defaultWeight <= 0f
                    || layer.blendingMode != AnimatorLayerBlendingMode.Override)
                {
                    continue;
                }
                if (!mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers)
                    && !mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers))
                {
                    continue;
                }
                InspectLayerCurves(layer, out bool body, out _);
                if (body)
                {
                    // A layer that deliberately drives the body — a kept GoGo locomotion
                    // replacement, say. Silently stripping its fingers would be us overruling
                    // the author, so this one is the user's call.
                    warned.Add(layer.name);
                    continue;
                }
                var stripped = new AvatarMask { name = mask.name + "_NoFingers" };
                for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                {
                    var bodyPart = (AvatarMaskBodyPart)part;
                    stripped.SetHumanoidBodyPartActive(bodyPart,
                        bodyPart != AvatarMaskBodyPart.LeftFingers
                        && bodyPart != AvatarMaskBodyPart.RightFingers
                        && mask.GetHumanoidBodyPartActive(bodyPart));
                }
                stripped.transformCount = mask.transformCount;
                for (int t = 0; t < mask.transformCount; t++)
                {
                    stripped.SetTransformPath(t, mask.GetTransformPath(t));
                    stripped.SetTransformActive(t, mask.GetTransformActive(t));
                }
                layer.avatarMask = stripped;
                repaired.Add(layer.name);
                changed = true;
            }

            if (changed)
            {
                master.layers = layers;
            }
            if (repaired.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{repaired.Count} layer(s) above the hand-pose layers stopped from overwriting gestures",
                    $"{string.Join(", ", repaired.Take(6))}{(repaired.Count > 6 ? ", …" : "")} — each sat " +
                    "above ChilloutVR's LeftHand/RightHand layers with a mask that let it write finger " +
                    "muscles, which on Override at full weight replaces whatever pose the gesture just " +
                    "played. Fingers were removed from their masks; everything else those layers animate " +
                    "is untouched. This is what makes a gesture look \"dead\" in game while the CCK " +
                    "Debugger shows the right clip playing at weight 1.");
            }
            if (warned.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{warned.Count} body layer(s) above the hand-pose layers can write finger muscles",
                    $"{string.Join(", ", warned.Take(6))}{(warned.Count > 6 ? ", …" : "")} — they animate " +
                    "the body deliberately, so nothing was changed. If hand gestures do not move your " +
                    "fingers in game, these are the layers to look at first: turn off fingers in their " +
                    "avatar mask, or lower their weight.");
            }
        }

        static void WarnLocomotionOverrides(List<AnimatorControllerLayer> vrcLayers, BridgeContext ctx)
        {
            foreach (var layer in vrcLayers)
            {
                if (layer.name == "LeftHand" || layer.name == "RightHand")
                {
                    continue; // hand pose layers are supposed to animate finger muscles
                }
                InspectLayerCurves(layer, out bool animatesBody, out _);
                if (animatesBody)
                {
                    // In keep-GoGo mode the Base/Additive/Action layers are SUPPOSED to drive
                    // the body — ChilloutVR's own locomotion layer was removed for them — so
                    // warning "this can override CVR's locomotion" about the layers doing the
                    // replacing is pure noise.
                    bool gogoReplacement = !ctx.Settings.stripGogoLoco && SystemStripper.AvatarUsesGogo(ctx)
                        && (layer.name.StartsWith("[Base]") || layer.name.StartsWith("[Additive]")
                            || layer.name.StartsWith("[Action]"));
                    if (!gogoReplacement)
                    {
                        ctx.Report.Warning(Category, $"Layer \"{layer.name}\" animates body muscles or root motion",
                            "It can override CVR's locomotion/pose. Review it; lower its weight or delete it if movement breaks.");
                    }
                }
            }
        }

        /// <summary>
        /// True for asset paths that some bake framework will DELETE on its next run — which
        /// entering play mode triggers, because both frameworks process the original avatar still
        /// sitting in the scene. Anything referenced from these folders must be cloned or it dies
        /// between the first play and the second.
        ///
        ///   * Packages/com.vrcfury — Fury's own temp, used when Fury runs standalone.
        ///   * Packages/nadena.dev.ndmf/__Generated — NDMF's `TemporaryAssetRoot`
        ///     (`AvatarProcessor.CleanTemporaryAssets` deletes the whole folder, and
        ///     `ApplyOnPlay` calls it). The moment Modular Avatar/NDMF is installed, VRCFury
        ///     runs as an NDMF plugin and bakes HERE instead of its own temp — which is how a
        ///     project that converted fine for weeks broke on the first avatar converted after
        ///     installing MA: every defence was watching com.vrcfury while the assets lived and
        ///     died in __Generated.
        /// </summary>
        internal static bool IsDoomedGeneratedPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            path = path.Replace('\\', '/');
            // Only the TEMP roots. The broad "Packages/com.vrcfury" prefix also matched the
            // installed package (com.vrcfury.vrcfury) — and clips that animate Fury component
            // properties legitimately reference its script GUIDs, so the audit flagged a stable
            // reference as doomed on every SPS avatar. The wiped folders, verified against both
            // frameworks' own code, are exactly these two:
            return path.StartsWith("Packages/com.vrcfury.temp", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Packages/nadena.dev.ndmf/__Generated", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads the SAVED controller off disk and resolves every external GUID it references.
        /// Two kinds of reference have destroyed avatars silently and both are named here:
        /// anything under Packages/com.vrcfury (Fury deletes that folder on its next build — and
        /// entering play mode with the original avatar still in the scene triggers exactly that
        /// build, which is how a conversion works on the first play and dies on the second), and
        /// anything that resolves to nothing at all. The serialized file is the ground truth the
        /// in-memory object graph can lie about, so this reads the text, not the objects.
        /// </summary>
        /// <summary>
        /// Teaches toggle animations to reach the generated physics.
        ///
        /// A MagicaCloth conversion hosts each chain on its own holder object at the avatar
        /// root, because MagicaCloth2 measures inertia at the cloth object (see
        /// MagicaClothWriter). The cost surfaced on an avatar with four hairstyles: the
        /// hair-swap animations activate each hairstyle's own objects, the PhysBone used to
        /// ride along with them, but the holder — on a path no animation had ever heard of —
        /// stayed disabled forever. Three of four hairstyles wore stiff hair in game.
        ///
        /// So, for every clip the final controller references: a GameObject active-state curve
        /// whose target is a converted PhysBone's object (or any ancestor of it) is copied onto
        /// the holder's own path — but ONLY when it activates (see the comment at the skip for
        /// why deactivations must not be mirrored) — and a VRCPhysBone m_Enabled curve is
        /// retargeted at the generated component's type, both directions. Added curves are
        /// byte-for-byte the original, so with Write Defaults the holder falls back to its
        /// scene default exactly like the hair objects themselves (an inactive style's holder
        /// is created inactive). Clips are cloned before modification; they may be the source
        /// avatar's own assets.
        /// </summary>
        static void RewirePhysicsToggles(AnimatorController master, BridgeContext ctx)
        {
            var chains = ctx.ConvertedPhysicsChains;
            // Zero chains still matters when style synthesis is on: an avatar whose ONLY
            // physics would be a synthesized rig must reach phase 1 below.
            if (chains == null || (chains.Count == 0 && !ctx.Settings.addPhysicsToRiggedStyles))
            {
                return;
            }

            var root = ctx.Target.transform;
            var pathCache = new Dictionary<string, Transform>();
            Transform Resolve(string path)
            {
                if (!pathCache.TryGetValue(path, out var t))
                {
                    pathCache[path] = t = string.IsNullOrEmpty(path) ? root : root.Find(path);
                }
                return t;
            }

            var rewired = new Dictionary<AnimationClip, AnimationClip>();
            int curvesAdded = 0, clipsTouched = 0;
            var physicslessStyles = new HashSet<Transform>();

            bool ChainInSubtree(Transform container)
            {
                foreach (var chain in chains)
                {
                    if (chain.Source != null &&
                        (chain.Source.transform == container || chain.Source.transform.IsChildOf(container)))
                    {
                        return true;
                    }
                }
                return false;
            }

            // A "self-contained rig": the majority of the container's skinned-mesh bones live
            // inside the container itself (an add-on hairstyle with its own little armature).
            // Clothing skinned to body bones doesn't qualify — its bones live outside. The rig
            // root reported back is the deepest common ancestor of the inside bones, which is
            // where a synthesized cloth would anchor.
            bool IsSelfContainedRig(Transform container, out Transform rigRoot)
            {
                rigRoot = null;
                var inside = new List<Transform>();
                int total = 0;
                foreach (var smr in container.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    foreach (var bone in smr.bones)
                    {
                        if (bone == null) continue;
                        total++;
                        if (bone == container || bone.IsChildOf(container)) inside.Add(bone);
                    }
                }
                if (total < 5 || inside.Count * 2 < total)
                {
                    return false;
                }
                rigRoot = inside[0];
                while (rigRoot != null && rigRoot != container.parent)
                {
                    bool coversAll = true;
                    foreach (var bone in inside)
                    {
                        if (bone != rigRoot && !bone.IsChildOf(rigRoot))
                        {
                            coversAll = false;
                            break;
                        }
                    }
                    if (coversAll) break;
                    rigRoot = rigRoot.parent;
                }
                if (rigRoot == null || rigRoot == container.parent)
                {
                    rigRoot = container;
                }
                return true;
            }

            // A toggled container carrying its own self-contained rig with no converted chain
            // never had physics in the source either — VRChat had nothing simulating those
            // bones. Saying so in the report turns "this hairstyle is broken" into "this
            // hairstyle was always rigid" without three rounds of testing: a tester's "Vampy"
            // hair is exactly this, 31 bare transforms and a mesh.
            void NotePhysicslessStyle(Transform container)
            {
                if (!physicslessStyles.Add(container) || ChainInSubtree(container))
                {
                    return;
                }
                if (!IsSelfContainedRig(container, out _))
                {
                    return;
                }
                string hint = ctx.Settings.addPhysicsToRiggedStyles
                    ? " (\"Add physics to toggled rigs that have none\" is on, but it needs the " +
                      "MagicaCloth2 physics target and MagicaCloth2 installed.)"
                    : " Turn on \"Add physics to toggled rigs that have none\" (Physics options) " +
                      "to synthesize a MagicaCloth here.";
                ctx.Report.Skipped(Category, container.name,
                    "This toggled object carries its own bone rig and skinned mesh, but NOTHING " +
                    "simulated those bones in the source — no PhysBone, so no physics existed in " +
                    "VRChat either, and none was converted." + hint);
            }

            AnimationClip Rewire(AnimationClip clip)
            {
                if (clip == null)
                {
                    return null;
                }
                if (rewired.TryGetValue(clip, out var known))
                {
                    return known;
                }

                Dictionary<EditorCurveBinding, AnimationCurve> additions = null;
                var existing = AnimationUtility.GetCurveBindings(clip);
                foreach (var binding in existing)
                {
                    bool objectToggle = binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive";
                    bool physBoneToggle = !objectToggle && binding.propertyName == "m_Enabled"
                        && binding.type != null && binding.type.Name == "VRCPhysBone";
                    if (!objectToggle && !physBoneToggle)
                    {
                        continue;
                    }
                    var animated = Resolve(binding.path);
                    if (animated == null)
                    {
                        continue;
                    }

                    bool anyChainInSubtree = false;
                    foreach (var chain in chains)
                    {
                        if (chain.Source == null || chain.Host == null)
                        {
                            continue;
                        }
                        var source = chain.Source.transform;
                        var host = chain.Host.transform;
                        EditorCurveBinding target;
                        if (objectToggle)
                        {
                            if (source != animated && !source.IsChildOf(animated))
                            {
                                continue;
                            }
                            anyChainInSubtree = true;
                            // Hosts INSIDE the toggled subtree already ride along (DynamicBone
                            // lives on the source object; and toggling the avatar root covers
                            // everything). Only an outside host needs the extra curve.
                            if (host == animated || host.IsChildOf(animated))
                            {
                                continue;
                            }
                            target = EditorCurveBinding.FloatCurve(
                                AnimationUtility.CalculateTransformPath(host, root),
                                typeof(GameObject), "m_IsActive");
                        }
                        else
                        {
                            // The PhysBone component itself was animated on/off; the component
                            // type died with the conversion, so retarget at what replaced it.
                            if (source != animated || chain.Physics == null)
                            {
                                continue;
                            }
                            target = EditorCurveBinding.FloatCurve(
                                AnimationUtility.CalculateTransformPath(host, root),
                                chain.Physics.GetType(), "m_Enabled");
                        }
                        bool alreadyDriven = false;
                        foreach (var have in existing)
                        {
                            if (have.path == target.path && have.type == target.type
                                && have.propertyName == target.propertyName)
                            {
                                alreadyDriven = true;
                                break;
                            }
                        }
                        if (alreadyDriven)
                        {
                            continue; // the clip already drives it deliberately
                        }
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve == null)
                        {
                            continue;
                        }
                        if (objectToggle && !CurveActivates(curve))
                        {
                            // Mirror ACTIVATIONS only, never deactivations. Turning a holder
                            // off with the style that owned it strangles any OTHER style whose
                            // bones ride the same chain: a tester's "Vampy" hair has no
                            // PhysBone of its own — its rig is grafted onto the base hair's
                            // simulated bones at bake time — and the base cloth being switched
                            // off with the base style's mesh left it rigid. A hidden style's
                            // cloth staying alive costs a little simulation of bones nobody
                            // sees; a shared chain being killed is a dead hairstyle. Where the
                            // avatar uses Write Defaults, holders still switch off for free —
                            // the added ON curve stops being written and the holder falls back
                            // to its scene default, exactly like the hair objects themselves.
                            continue;
                        }
                        if (additions == null)
                        {
                            additions = new Dictionary<EditorCurveBinding, AnimationCurve>();
                        }
                        additions[target] = curve;
                    }
                    if (objectToggle && !anyChainInSubtree)
                    {
                        var activation = AnimationUtility.GetEditorCurve(clip, binding);
                        if (activation != null && CurveActivates(activation))
                        {
                            NotePhysicslessStyle(animated);
                        }
                    }
                }

                if (additions == null)
                {
                    rewired[clip] = clip;
                    return clip;
                }
                var clone = UnityEngine.Object.Instantiate(clip);
                clone.name = clip.name;
                clone.hideFlags = HideFlags.None;
                foreach (var pair in additions)
                {
                    AnimationUtility.SetEditorCurve(clone, pair.Key, pair.Value);
                }
                curvesAdded += additions.Count;
                clipsTouched++;
                rewired[clip] = clone;
                return clone;
            }

            static bool CurveActivates(AnimationCurve curve)
            {
                foreach (var key in curve.keys)
                {
                    if (key.value > 0.5f)
                    {
                        return true;
                    }
                }
                return false;
            }

            Motion RewireMotion(Motion motion)
            {
                if (motion is AnimationClip clip)
                {
                    return Rewire(clip);
                }
                if (motion is BlendTree tree)
                {
                    var children = tree.children;
                    bool changed = false;
                    for (int i = 0; i < children.Length; i++)
                    {
                        var replaced = RewireMotion(children[i].motion);
                        if (!ReferenceEquals(replaced, children[i].motion))
                        {
                            children[i].motion = replaced;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        // Assigning children re-derives thresholds under automatic mode; pin
                        // manual mode for the write, exactly like the deep copier does.
                        bool auto = tree.useAutomaticThresholds;
                        tree.useAutomaticThresholds = false;
                        tree.children = children;
                        tree.useAutomaticThresholds = auto;
                    }
                }
                return motion;
            }

#if AVATARBRIDGE_MAGICA
            // Phase 1, before any curve is copied: "Add physics to toggled rigs that have
            // none". Every container an animation ACTIVATES is a style; a style that is a
            // self-contained rig with no converted chain gets a synthesized MagicaCloth. Done
            // here rather than in the physics pass because "toggled" is the narrowing fact —
            // it is what separates an add-on hairstyle from every rigged prop on the avatar —
            // and only the animator knows it. The new chain registers itself, so phase 2 below
            // wires its holder to the style's activation curves like any other chain.
            if (ctx.Settings.addPhysicsToRiggedStyles
                && ctx.Settings.physicsTarget == PhysicsTarget.MagicaCloth2)
            {
                var activated = new HashSet<Transform>();
                void CollectActivations(Motion motion)
                {
                    if (motion is AnimationClip clip)
                    {
                        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                        {
                            if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive")
                            {
                                continue;
                            }
                            var curve = AnimationUtility.GetEditorCurve(clip, binding);
                            if (curve == null || !CurveActivates(curve))
                            {
                                continue;
                            }
                            var target = Resolve(binding.path);
                            if (target != null)
                            {
                                activated.Add(target);
                            }
                        }
                    }
                    else if (motion is BlendTree tree)
                    {
                        foreach (var child in tree.children)
                        {
                            CollectActivations(child.motion);
                        }
                    }
                }
                foreach (var layer in master.layers)
                {
                    WalkMachines(layer.stateMachine, machine =>
                    {
                        foreach (var child in machine.states)
                        {
                            CollectActivations(child.state.motion);
                        }
                    });
                }
                foreach (var container in activated)
                {
                    if (ChainInSubtree(container) || !IsSelfContainedRig(container, out var rigRoot))
                    {
                        continue;
                    }
                    MagicaClothWriter.WriteSynthesized(ctx, rigRoot);
                }
            }
#endif

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        child.state.motion = RewireMotion(child.state.motion);
                    }
                });
            }

            if (clipsTouched > 0)
            {
                ctx.Report.Converted(Category,
                    $"{curvesAdded} toggle curve(s) re-wired to generated physics in {clipsTouched} clip(s)",
                    "Animations that activated a converted PhysBone's object or component (hair swaps, " +
                    "outfit toggles) now activate the generated physics too. Without this, a chain " +
                    "belonging to a style that was inactive at conversion time could never wake up — " +
                    "its cloth lives on its own object at the avatar root, on a path the original " +
                    "animations never animated. Only activations are mirrored: styles that share " +
                    "another style's simulated bones (add-on hair grafted onto a base rig) must not " +
                    "have that chain switched off with the base style's mesh, so a hidden style's " +
                    "cloth may keep simulating — invisible, and harmless.");
            }
        }

        /// <summary>True when every segment of the path is purely digits — a pre-hashed path.
        /// ChilloutVR's own locomotion clips ship this way (the client binds them by hash), so
        /// they can neither be audited nor repaired by string comparison.</summary>
        static bool IsHashedPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            foreach (var segment in path.Split('/'))
            {
                if (segment.Length == 0)
                {
                    return false;
                }
                foreach (var c in segment)
                {
                    if (c < '0' || c > '9')
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Repairs curve paths broken by a renamed bone, when the repair is provable.
        ///
        /// The motivating case: a tail-wag clip binding "Armature/Hips/Tail_Root/Tail.001"
        /// against an avatar whose bone is "Armature/Hips/Tail" — someone renamed the bone
        /// after the animation was authored, every tail curve went silent, and it played as
        /// silence in VRChat too. The repair rule is deliberately strict: a dead path is
        /// rewritten only when the avatar contains EXACTLY ONE transform at the same depth
        /// whose path matches every segment but one. One candidate is a proof; two is a guess,
        /// and a guess would wag the wrong bones — ambiguous paths are left for the audit to
        /// report. Clips are cloned before modification, as everywhere else.
        /// </summary>
        static void RepairClipPaths(AnimatorController master, BridgeContext ctx)
        {
            var root = ctx.Target.transform;

            var byDepth = new Dictionary<int, List<string[]>>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root)
                {
                    continue;
                }
                var segs = AnimationUtility.CalculateTransformPath(t, root).Split('/');
                if (!byDepth.TryGetValue(segs.Length, out var list))
                {
                    byDepth[segs.Length] = list = new List<string[]>();
                }
                list.Add(segs);
            }

            var pathFix = new Dictionary<string, string>();
            string Repair(string path)
            {
                if (pathFix.TryGetValue(path, out var known))
                {
                    return known;
                }
                string result = null;
                var segs = path.Split('/');
                if (byDepth.TryGetValue(segs.Length, out var candidates))
                {
                    bool ambiguous = false;
                    foreach (var cs in candidates)
                    {
                        int mismatch = 0;
                        for (int i = 0; i < segs.Length && mismatch <= 1; i++)
                        {
                            if (!string.Equals(segs[i], cs[i], StringComparison.Ordinal))
                            {
                                mismatch++;
                            }
                        }
                        if (mismatch <= 1)
                        {
                            if (result != null)
                            {
                                ambiguous = true;
                                break;
                            }
                            result = string.Join("/", cs);
                        }
                    }
                    if (ambiguous)
                    {
                        result = null;
                    }
                }
                pathFix[path] = result;
                return result;
            }

            var repaired = new Dictionary<AnimationClip, AnimationClip>();
            var rows = new List<string>();

            AnimationClip Fix(AnimationClip clip)
            {
                if (clip == null)
                {
                    return null;
                }
                if (repaired.TryGetValue(clip, out var done))
                {
                    return done;
                }
                List<(EditorCurveBinding oldB, EditorCurveBinding newB, bool objRef)> moves = null;
                string exampleOld = null, exampleNew = null;
                void Consider(EditorCurveBinding binding, bool objRef)
                {
                    if (string.IsNullOrEmpty(binding.path) || IsHashedPath(binding.path))
                    {
                        return;
                    }
                    if (root.Find(binding.path) != null)
                    {
                        return;
                    }
                    var fixedPath = Repair(binding.path);
                    if (fixedPath == null)
                    {
                        return;
                    }
                    var moved = binding;
                    moved.path = fixedPath;
                    if (moves == null)
                    {
                        moves = new List<(EditorCurveBinding, EditorCurveBinding, bool)>();
                    }
                    moves.Add((binding, moved, objRef));
                    if (exampleOld == null)
                    {
                        exampleOld = binding.path;
                        exampleNew = fixedPath;
                    }
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    Consider(binding, false);
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    Consider(binding, true);
                }
                if (moves == null)
                {
                    repaired[clip] = clip;
                    return clip;
                }
                var clone = UnityEngine.Object.Instantiate(clip);
                clone.name = clip.name;
                clone.hideFlags = HideFlags.None;
                foreach (var (oldB, newB, objRef) in moves)
                {
                    if (objRef)
                    {
                        var keys = AnimationUtility.GetObjectReferenceCurve(clip, oldB);
                        AnimationUtility.SetObjectReferenceCurve(clone, oldB, null);
                        AnimationUtility.SetObjectReferenceCurve(clone, newB, keys);
                    }
                    else
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, oldB);
                        AnimationUtility.SetEditorCurve(clone, oldB, null);
                        AnimationUtility.SetEditorCurve(clone, newB, curve);
                    }
                }
                repaired[clip] = clone;
                rows.Add($"\"{clip.name}\" ({moves.Count} curve(s), e.g. \"{exampleOld}\" -> \"{exampleNew}\")");
                return clone;
            }

            Motion FixMotion(Motion motion)
            {
                if (motion is AnimationClip clip)
                {
                    return Fix(clip);
                }
                if (motion is BlendTree tree)
                {
                    var children = tree.children;
                    bool changed = false;
                    for (int i = 0; i < children.Length; i++)
                    {
                        var replacedChild = FixMotion(children[i].motion);
                        if (!ReferenceEquals(replacedChild, children[i].motion))
                        {
                            children[i].motion = replacedChild;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        bool auto = tree.useAutomaticThresholds;
                        tree.useAutomaticThresholds = false;
                        tree.children = children;
                        tree.useAutomaticThresholds = auto;
                    }
                }
                return motion;
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        child.state.motion = FixMotion(child.state.motion);
                    }
                });
            }

            if (rows.Count > 0)
            {
                ctx.Report.Approximated(Category,
                    $"{rows.Count} clip(s) had broken curve paths repaired",
                    string.Join("; ", rows.Take(8)) + (rows.Count > 8 ? "; …" : "") + ". These " +
                    "bindings pointed at object names that don't exist on the avatar — usually a " +
                    "bone renamed after the animation was authored — and played as silence in " +
                    "VRChat too. Each was rewritten only because exactly ONE transform at the same " +
                    "depth matches every other segment of the path; anything ambiguous was left " +
                    "alone and appears in the broken-paths warning instead.");
            }
        }

        /// <summary>
        /// Warns when a game-fed parameter is animated by a clip as an animated animator
        /// parameter. The client builds each parameter's definition with
        /// Animator.IsParameterControlledByCurve (decompiled: AvatarParam → IsReadOnly), and it
        /// REFUSES to write read-only parameters — so a single AAP curve on GestureLeftIdx or
        /// MovementX freezes that parameter in game forever, on this avatar only, while the
        /// editor (where the tester tool writes directly) behaves perfectly. That exact
        /// asymmetry burned days of tester rounds; whether or not it is any given avatar's
        /// fault, the report must name it.
        /// </summary>
        static void AuditCurveControlledGameParameters(AnimatorController master, BridgeContext ctx)
        {
            var gameFed = new HashSet<string>(CvrCoreParameters);
            gameFed.UnionWith(StreamFedParameters);
            gameFed.Add("VisemeLoudness");
            gameFed.Add("Upright");

            var offenders = new Dictionary<string, List<string>>();
            var seen = new HashSet<AnimationClip>();

            void Audit(Motion motion)
            {
                if (motion is BlendTree tree)
                {
                    foreach (var child in tree.children)
                    {
                        Audit(child.motion);
                    }
                    return;
                }
                if (!(motion is AnimationClip clip) || !seen.Add(clip))
                {
                    return;
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(Animator) || !string.IsNullOrEmpty(binding.path))
                    {
                        continue;
                    }
                    string bare = binding.propertyName.TrimStart('#');
                    if (!gameFed.Contains(bare))
                    {
                        continue;
                    }
                    if (!offenders.TryGetValue(binding.propertyName, out var clips))
                    {
                        offenders[binding.propertyName] = clips = new List<string>();
                    }
                    if (!clips.Contains(clip.name))
                    {
                        clips.Add(clip.name);
                    }
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        Audit(child.state.motion);
                    }
                });
            }

            foreach (var offender in offenders)
            {
                ctx.Report.Warning(Category,
                    $"Game-fed parameter \"{offender.Key}\" is animated by a clip — the game will NEVER write it",
                    $"Clip(s): {string.Join(", ", offender.Value.Take(5))}" +
                    (offender.Value.Count > 5 ? ", …" : "") + ". ChilloutVR marks curve-controlled " +
                    "parameters read-only and refuses to feed them (decompiled: " +
                    "IsParameterControlledByCurve → IsReadOnly), so this parameter sits frozen in " +
                    "game while editor testing works perfectly. Remove the curve from those clips, " +
                    "or rename the animated parameter apart from the game-fed one.");
            }
        }

        /// <summary>
        /// Names every clip whose curves target paths that don't exist on this avatar.
        ///
        /// Unity plays such curves as silence: no error, no log line, the feature just doesn't
        /// happen — and crucially it didn't happen in VRChat either, UNLESS a build-time tool
        /// was rewriting the paths at upload. A tester's tail-wag clip bound "Tail.001/…"
        /// against a tail living at "Armature/Hips/Tail/…": authored for a different root,
        /// only ever functional through VRCFury-style path rewriting, and the conversion —
        /// which faithfully preserved both the clip and the hierarchy — inherited the mismatch
        /// invisibly. This audit can't fix a path (guessing would move the wrong bones); it
        /// makes the mismatch loud and says what to check.
        /// </summary>
        static void AuditClipBindings(AnimatorController master, BridgeContext ctx)
        {
            var root = ctx.Target.transform;
            var resolveCache = new Dictionary<string, bool>();
            bool Resolves(string path)
            {
                if (string.IsNullOrEmpty(path))
                {
                    return true;
                }
                if (!resolveCache.TryGetValue(path, out var ok))
                {
                    resolveCache[path] = ok = root.Find(path) != null;
                }
                return ok;
            }

            var seen = new HashSet<AnimationClip>();
            var broken = new List<(string clip, int dead, int total, string example)>();

            void Audit(Motion motion)
            {
                if (motion is BlendTree tree)
                {
                    foreach (var child in tree.children)
                    {
                        Audit(child.motion);
                    }
                    return;
                }
                if (!(motion is AnimationClip clip) || !seen.Add(clip))
                {
                    return;
                }
                int dead = 0, total = 0;
                string example = null;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip)
                             .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                {
                    // Animator-type bindings with an empty path are animated animator
                    // parameters, not scene objects; they have no path to resolve. Pre-hashed
                    // numeric paths (the CCK's own locomotion clips ship this way, bound by
                    // hash at runtime) cannot be audited by string lookup and are healthy.
                    if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path))
                    {
                        continue;
                    }
                    if (IsHashedPath(binding.path))
                    {
                        continue;
                    }
                    total++;
                    if (!Resolves(binding.path))
                    {
                        dead++;
                        example = example ?? binding.path;
                    }
                }
                if (dead > 0)
                {
                    broken.Add((clip.name, dead, total, example));
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        Audit(child.state.motion);
                    }
                });
            }

            if (broken.Count == 0)
            {
                return;
            }
            broken.Sort((a, b) => b.dead.CompareTo(a.dead));
            var lines = broken.Take(8)
                .Select(b => $"\"{b.clip}\" ({b.dead} of {b.total}, e.g. \"{b.example}\")");
            ctx.Report.Warning(Category,
                $"{broken.Count} clip(s) animate paths that don't exist on this avatar",
                string.Join("; ", lines) + (broken.Count > 8 ? "; …" : "") + ". Unity plays these " +
                "curves as silence — and did in VRChat too, unless a build-time tool (VRCFury path " +
                "rewriting, Modular Avatar) fixed the paths at upload. If one of these features " +
                "worked in VRChat, make sure the package it was built with is INSTALLED in this " +
                "project and convert again, so its bake runs first. Clips animating objects a " +
                "stripped system removed also land here, and those are fine.");
        }

        // Serialized guid sets of the source controllers, captured before any merging.
        static readonly HashSet<string> _sourceControllerGuids = new HashSet<string>();

        static void CollectSerializedGuids(string assetPath, HashSet<string> into)
        {
            try
            {
                if (string.IsNullOrEmpty(assetPath))
                {
                    return;
                }
                string full = System.IO.Path.GetFullPath(assetPath);
                if (!System.IO.File.Exists(full))
                {
                    return;
                }
                // Full PPtr syntax only — {fileID: N, guid: X, type: N}. A bare "guid:" grep
                // matched guid-LOOKING text inside string fields: an avatar with a missing
                // prefab gets Unity's literal "(Missing Prefab with guid: …)" object name,
                // that name lands in generated mask transform paths, and the audit read its
                // own mask's path string as a dead asset reference.
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex
                    .Matches(System.IO.File.ReadAllText(full),
                        @"\{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d+\}"))
                {
                    into.Add(match.Groups[1].Value);
                }
            }
            catch
            {
                // Unreadable source: the audit just loses inherited/introduced attribution.
            }
        }

        /// <summary>
        /// Reads the FINAL saved controller file and judges every serialized guid. Runs from
        /// BridgeConverter after AnimationSelfContainer, so it sees the file the user will
        /// actually upload. Three verdicts: a reference into bake-temp or one the conversion
        /// introduced is OUR bug (Error, don't upload); a reference that was already dead in
        /// the source controllers is inherited (Warning — the same motion was None in VRChat
        /// too, nothing broke here).
        /// </summary>
        internal static void AuditSerializedReferences(BridgeContext ctx)
        {
            string controllerPath = ctx.MergedController != null
                ? AssetDatabase.GetAssetPath(ctx.MergedController)
                : null;
            var guids = new HashSet<string>();
            CollectSerializedGuids(controllerPath, guids);
            if (guids.Count == 0)
            {
                return;
            }

            int intoTemp = 0, introduced = 0, inherited = 0;
            string badSample = null, inheritedSample = null;
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    if (_sourceControllerGuids.Contains(guid))
                    {
                        inherited++;
                        inheritedSample = inheritedSample ?? guid;
                    }
                    else
                    {
                        introduced++;
                        badSample = badSample ?? guid;
                    }
                }
                else if (IsDoomedGeneratedPath(path))
                {
                    intoTemp++;
                    badSample = badSample ?? path;
                }
            }

            if (intoTemp > 0 || introduced > 0)
            {
                ctx.Report.Error(Category,
                    $"The saved controller references {intoTemp} bake-temp (VRCFury/NDMF) and {introduced} unresolvable asset(s) the conversion introduced",
                    $"e.g. {badSample}. VRCFury deletes its temp folder on its next build — which entering " +
                    "play mode triggers if the original avatar is still in the scene — and an " +
                    "unresolvable reference is already dead. Either way those animations will stop " +
                    "working after the next play or Fury build. This is a conversion bug: please report " +
                    "it with this file attached. Do not upload this conversion.");
            }
            if (inherited > 0)
            {
                ctx.Report.Warning(Category,
                    $"{inherited} dead asset reference(s) inherited from the source avatar",
                    $"e.g. {inheritedSample}. The source controllers already reference an asset that " +
                    "doesn't exist in this project, so the same motion was None in VRChat too — usually " +
                    "a package or animation the avatar shipped with that was never imported here. " +
                    "Nothing broke in conversion and uploading is safe; those animations play as " +
                    "stillness on both platforms. To revive them, import the missing package and " +
                    "convert again.");
            }
        }

        /// <summary>
        /// Counts every non-null motion reference reachable from the controller's layers — state
        /// motions and blend-tree children, recursively. Cheap, and comparing the count across a
        /// save is the only reliable detector for Unity's silent DontSave amputation: a dangling
        /// reference reloads as null, so the delta IS the number of motions that died in transit.
        /// </summary>
        static int CountMotionReferences(AnimatorController controller)
        {
            int count = 0;
            void CountMotion(Motion motion)
            {
                if (motion == null)
                {
                    return;
                }
                count++;
                if (motion is BlendTree tree)
                {
                    foreach (var child in tree.children)
                    {
                        CountMotion(child.motion);
                    }
                }
            }
            foreach (var layer in controller.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        CountMotion(child.state.motion);
                    }
                });
            }
            return count;
        }

        static IEnumerable<AnimationClip> CollectClips(Motion motion)
        {
            if (motion is AnimationClip clip)
            {
                yield return clip;
            }
            else if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                {
                    foreach (var nested in CollectClips(child.motion))
                    {
                        yield return nested;
                    }
                }
            }
        }

        /// <summary>
        /// Two layers must never share one state machine: the later layer sits in its
        /// default (usually empty) state and, with write-defaults on, overwrites whatever
        /// the earlier one animates — silently killing that toggle.
        /// </summary>
        static void DeduplicateLayers(AnimatorController master, BridgeContext ctx)
        {
            var seenMachines = new HashSet<AnimatorStateMachine>();
            var kept = new List<AnimatorControllerLayer>();
            var dropped = new List<string>();
            foreach (var layer in master.layers)
            {
                if (layer.stateMachine != null && !seenMachines.Add(layer.stateMachine))
                {
                    dropped.Add(layer.name);
                    continue;
                }
                kept.Add(layer);
            }
            if (dropped.Count > 0)
            {
                master.layers = kept.ToArray();
                ctx.Report.Warning(Category, $"Removed {dropped.Count} duplicate layer(s)",
                    string.Join(", ", dropped.Distinct()) + " — duplicates would have overwritten the working layer.");
            }
        }

        static int _gestureConditionsRedirected;

        /// <summary>
        /// Both Native and DragonSkyRunner modes replace the avatar's face tracking, so the
        /// existing FT rig baked in from a VRCFury FT template (VRCFaceTracking / Jerry's /
        /// Pawlygon / OSCmooth) is removed — its FT-dominated animator layers and their
        /// now-unreferenced FT parameters. None mode keeps them.
        /// </summary>
        static void StripExistingFaceTracking(AnimatorController master, List<AnimatorControllerLayer> vrcLayers, BridgeContext ctx)
        {
            if (ctx.Settings.faceTrackingMode == FaceTrackingMode.None)
            {
                return;
            }
            bool IsFt(string name) => FaceTrackingParameters.IsFaceTracking(name);

            var removedMachines = new HashSet<AnimatorStateMachine>();
            foreach (var layer in vrcLayers.ToList())
            {
                var refs = SystemStripper.CollectParameterRefs(layer.stateMachine);
                if (refs.Count == 0)
                {
                    continue;
                }
                int ftRefs = refs.Count(IsFt);
                if (ftRefs > 0 && ftRefs >= refs.Count * 0.6f)
                {
                    removedMachines.Add(layer.stateMachine);
                    vrcLayers.Remove(layer);
                }
            }
            if (removedMachines.Count > 0)
            {
                master.layers = master.layers
                    .Where(l => l.stateMachine == null || !removedMachines.Contains(l.stateMachine))
                    .ToArray();
            }

            // Dropping the FT layers isn't enough on its own. VRCFury emits a "Defaults" layer as
            // one big Direct blend tree that writes every parameter on the avatar, FT included —
            // so every FT parameter stays "still referenced" by a layer that is only ~2% FT and is
            // rightly kept. That left the whole FT/v2 parameter set in the converted avatar even
            // though its rig was gone. Pruning the stripped parameters out of Direct blend trees
            // first — the same treatment GoGo and SPS already get — releases them.
            SystemStripper.PruneDirectBlendTrees(ctx, master, vrcLayers, IsFt);

            var stillReferenced = new HashSet<string>();
            foreach (var layer in master.layers)
            {
                stillReferenced.UnionWith(SystemStripper.CollectParameterRefs(layer.stateMachine));
            }
            var parameters = master.parameters;
            int before = parameters.Length;
            master.parameters = parameters.Where(p => !IsFt(p.name) || stillReferenced.Contains(p.name)).ToArray();
            int removedParams = before - master.parameters.Length;

            if (removedMachines.Count > 0 || removedParams > 0)
            {
                ctx.Report.Converted("Face tracking",
                    $"Removed the avatar's existing FT rig — {removedMachines.Count} layer(s), {removedParams} parameter(s)",
                    "This mode provides its own face tracking, so the baked-in FT animator was removed " +
                    "to avoid fighting it for the same blendshapes.");
            }
        }

        static AvatarMask _handLeftMask, _handRightMask, _handsOnlyMask, _musclesOnlyMask, _noMuscleMask, _fingersOnlyMask;

        static AvatarMask GetHandsOnlyMask() =>
            _handsOnlyMask = _handsOnlyMask != null ? _handsOnlyMask
                : BuildMask("AvatarBridge_HandsOnly", AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers);

        /// <summary>
        /// Blocks humanoid muscles while leaving everything else alone.
        ///
        /// Every transform in the avatar is listed and explicitly enabled rather than leaving the
        /// list empty. An empty transform list is ambiguous — Unity can read it as "no transform
        /// restriction" or as "no transforms at all", and the difference is the whole rig for an
        /// avatar driven through IK target transforms rather than muscles, which is exactly what
        /// a FinalIK quadruped is. Spelling every transform out removes the question.
        ///
        /// Blendshape, GameObject-active and material curves are unaffected either way; avatar
        /// masks only ever govern transforms and muscles.
        /// </summary>
        static AvatarMask GetNoMuscleMask(BridgeContext ctx)
        {
            return _noMuscleMask != null ? _noMuscleMask
                : _noMuscleMask = BuildRigMask("AvatarBridge_NoMuscles", ctx);
        }

        /// <summary>Fingers, for layers that pose hands and nothing else. Same reasoning.</summary>
        static AvatarMask GetFingersOnlyMask(BridgeContext ctx)
        {
            return _fingersOnlyMask != null ? _fingersOnlyMask
                : _fingersOnlyMask = BuildRigMask("AvatarBridge_FingersOnly", ctx,
                    AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers);
        }

        static AvatarMask BuildRigMask(string name, BridgeContext ctx, params AvatarMaskBodyPart[] activeParts)
        {
            var mask = BuildMask(name, activeParts);
            var root = ctx.Target != null ? ctx.Target.transform : null;
            if (root != null)
            {
                mask.AddTransformPath(root, true);
                for (int i = 0; i < mask.transformCount; i++)
                {
                    mask.SetTransformActive(i, true);
                }
            }
            return mask;
        }

        static AvatarMask ReplaceVrcMask(AvatarMask mask, BridgeContext ctx)
        {
            if (mask == null)
            {
                return null;
            }
            switch (mask.name)
            {
                case "vrc_Hand Left":
                    return _handLeftMask = _handLeftMask != null ? _handLeftMask
                        : BuildMask("AvatarBridge_HandLeft", AvatarMaskBodyPart.LeftFingers);
                case "vrc_Hand Right":
                    return _handRightMask = _handRightMask != null ? _handRightMask
                        : BuildMask("AvatarBridge_HandRight", AvatarMaskBodyPart.RightFingers);
                case "vrc_HandsOnly":
                    return GetHandsOnlyMask();
                case "vrc_MusclesOnly":
                    if (_musclesOnlyMask == null)
                    {
                        _musclesOnlyMask = BuildMask("AvatarBridge_MusclesOnly",
                            AvatarMaskBodyPart.Root, AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head,
                            AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg,
                            AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                            AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers);
                    }
                    return _musclesOnlyMask;
                default:
                    return mask;
            }
        }

        static AvatarMask BuildMask(string name, params AvatarMaskBodyPart[] activeParts)
        {
            var mask = new AvatarMask { name = name };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, activeParts.Contains((AvatarMaskBodyPart)i));
            }
            return mask;
        }

        // ------------------------------------------------------------------ utils ----

        public static void ResetMaskCache()
        {
            _handLeftMask = _handRightMask = _handsOnlyMask = _musclesOnlyMask = null;
            _noMuscleMask = _fingersOnlyMask = null;
        }

        static void WalkMachines(AnimatorStateMachine machine, Action<AnimatorStateMachine> visit)
        {
            if (machine == null)
            {
                return;
            }
            visit(machine);
            foreach (var child in machine.stateMachines)
            {
                WalkMachines(child.stateMachine, visit);
            }
        }

        static string GetCvrHandLayerName(VRCAvatarDescriptor.AnimLayerType id, AnimatorControllerLayer srcLayer)
        {
            if (id != VRCAvatarDescriptor.AnimLayerType.Gesture)
            {
                return null;
            }
            string maskName = srcLayer.avatarMask != null ? srcLayer.avatarMask.name : "";
            string layerName = srcLayer.name.ToLowerInvariant();
            if (maskName == "vrc_Hand Left" || layerName.Contains("left"))
            {
                return "LeftHand";
            }
            if (maskName == "vrc_Hand Right" || layerName.Contains("right"))
            {
                return "RightHand";
            }
            return null;
        }

        static string MakeUniqueLayerName(List<AnimatorControllerLayer> layers, string name)
        {
            string candidate = name;
            int suffix = 2;
            while (layers.Any(l => l.name == candidate))
            {
                candidate = $"{name} {suffix++}";
            }
            return candidate;
        }

        static string SanitizeFileName(string name)
        {
            // An asset's name is free text and can be empty; a file name can be neither.
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Asset";
            }
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}
#endif
