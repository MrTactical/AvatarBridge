#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text.RegularExpressions;
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
            "Upright", "TrackingType", "EyeHeightAsMeters"
            // The rest of VRChat's scale family — ScaleFactor, ScaleFactorInverse,
            // EyeHeightAsPercent, ScaleModified — is deliberately NOT here: FeedScaleParameters
            // derives them from EyeHeightAsMeters by pure arithmetic, which every client can do
            // for every copy. Local ("#") + recomputed beats synced by 97 bits with identical
            // values, the same trade FeedVelocityMagnitude makes.
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
        //
        // The scale family (EyeHeightAsMeters, ScaleFactor, ScaleFactorInverse,
        // EyeHeightAsPercent, ScaleModified) left this list in 3.5.0: EyeHeightAsMeters is fed
        // by a CVRParameterStream from the client's AvatarHeight — the calibrated avatar height
        // in metres, the very number AvatarUpright divides by — and the other four are exact
        // arithmetic on it (FeedScaleParameters), using the conversion-time viewpoint height as
        // the baseline VRChat calls "default scale".
        static readonly HashSet<string> KnownUnsupportedVrcParameters = new HashSet<string>
        {
            "Earmuffs", "AngularY",
            "AvatarVersion", "VelocityMagnitude", "GroundProximity", "InStation",
            "IsAnimatorEnabled"
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

            // Before the merge loop: the Action transplant below and LocomotionGrafter both
            // prepare clips through the grafter's per-conversion clone caches.
            LocomotionGrafter.ResetClones();
            DroppedPlayAudio.Clear();
            DroppedPlayAudioCount = 0;
            DroppedPoseSpace.Clear();
            DroppedPoseSpaceCount = 0;

            AnimatorController master = LoadBaseController(ctx, convertingGestureLayer);
            var masterLayers = master.layers.ToList();
            var vrcLayers = new List<AnimatorControllerLayer>();

            // In keep-GoGo mode the Base/Additive/Action layers REPLACE ChilloutVR's locomotion,
            // so they are supposed to run at full weight and drive the body.
            bool gogoDrivesLocomotion = !ctx.Settings.stripGogoLoco && SystemStripper.AvatarUsesGogo(ctx);
            int actionLayersRested = 0;
            // Action layers kept LIVE because the avatar drives them itself — see the weight
            // decision below and the report entry at the end of the merge.
            var actionFeatures = new List<string>();
            var actionMoved = new List<string>();

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
                        //
                        // ALWAYS weight 0, including for Action layers that carry a feature of the
                        // avatar's own — and that "including" was fought for and lost, so record
                        // the whole retreat. 3.4.20–3.4.24 tried to keep feature layers live at
                        // weight 1 with their non-feature states made inert. Every variant failed
                        // on the same wall: Unity gives a layer no way to YIELD. An inert state
                        // with Write Defaults off makes the layer HOLD the last muscles any live
                        // state wrote — the avatar froze mid-pose, confirmed by watching the stuck
                        // machine sit in an inert state while the pose persisted — and Write
                        // Defaults on would assert the rest pose over locomotion instead. VRChat
                        // resolves this with runtime playable-weight control, which ChilloutVR
                        // does not have. So the pose portion of such a feature is a platform wall,
                        // and it is REPORTED as one rather than half-shipped: the FX portion (mesh
                        // swaps, materials — the visible part) lives in other layers and works.
                        clone.defaultWeight = 0f;
                        actionLayersRested++;
                        if (ActionLayerDrivesOwnFeature(clone, out string byWhat))
                        {
                            // The POSES go where ChilloutVR keeps poses: inside its own
                            // locomotion layer. The clone stays merged at weight 0 so its
                            // parameter drivers keep firing on schedule.
                            int moved = TransplantActionFeature(master, masterLayers, clone, srcLayer, ctx);
                            if (moved > 0)
                            {
                                actionMoved.Add($"\"{clone.name}\" (driven by {byWhat}, {moved} pose state(s))");
                            }
                            else
                            {
                                actionFeatures.Add($"\"{clone.name}\" (driven by {byWhat})");
                            }
                        }
                    }
                    clone.avatarMask = ReplaceVrcMask(clone.avatarMask, ctx);
                    masterLayers.Add(clone);
                    vrcLayers.Add(clone);
                }
                ctx.Report.Converted(Category, $"{id} layer merged", $"{controller.layers.Length} sub-layers");
            }

            if (actionMoved.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{actionMoved.Count} Action-layer feature(s) moved into ChilloutVR's locomotion layer",
                    $"{string.Join("; ", actionMoved)}. VRChat plays these full-body sequences from its " +
                    "Action playable, raising its weight at runtime — weight control ChilloutVR doesn't " +
                    "have, and a separate layer can neither yield when idle nor assert without freezing. " +
                    "ChilloutVR's own home for full-body poses is its Locomotion/Emotes layer, so the " +
                    "pose states were rebuilt THERE: they take over from locomotion exactly while their " +
                    "driving conditions hold, and hand back to it on the same conditions the original " +
                    "used to fade its layer out. The original layer stays merged at weight 0 so its " +
                    "parameter drivers keep firing on schedule. Not carried over: VRChat's tracking " +
                    "control (IK cut-off during the sequence) and its half-second weight fades, so " +
                    "entering and leaving the pose blends over a fixed quarter second instead.");
            }
            if (actionFeatures.Count > 0)
            {
                ctx.Report.Approximated(Category,
                    $"{actionFeatures.Count} Action layer(s) carry a feature this platform cannot pose",
                    $"{string.Join("; ", actionFeatures)}. VRChat's Action playable sits at weight 0 and " +
                    "is raised at runtime by behaviours while a sequence plays — that runtime weight " +
                    "control is the one piece ChilloutVR does not have, and without it there is no safe " +
                    "weight for such a layer: at 0 its full-body poses never show, at 1 Unity offers no " +
                    "way for the layer to yield between sequences, so the avatar freezes in its last pose " +
                    "(tried, at length; it is a wall, not a bug). The layer is merged at weight 0, so " +
                    "everything OUTSIDE it still works — mesh swaps, materials and toggles live in FX " +
                    "layers and carry the visible part of the feature. What is lost is the full-body pose " +
                    "while the sequence plays. Raising the layer's weight by hand in the Animator window " +
                    "shows the poses but WILL freeze the body on the pose it last played.");
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
            // After the stripper (keep-GoGo mode may have removed the CCK locomotion layer this
            // grafts into), before anything renames parameters — it reads the SOURCE controllers
            // off the descriptor, where VelocityX/VelocityZ still carry VRChat's names.
            LocomotionGrafter.Run(ctx, master);
            StripExistingFaceTracking(master, vrcLayers, ctx);
            ReplaceAnimatorBlink(master, ctx);
            ToggleNativizer.Run(ctx, master, vrcLayers);
            // Before RenamePass, so the menu entries' machineNames still line up with the
            // animator parameter names; and before CompactIntDropdowns, which needs the
            // dropdown parameters to already be Ints.
            ParameterTypeInference.Run(master, ctx);
            RenamePass(master, vrcLayers, ctx);
            ApplyParameterDefaults(master, ctx);
            ReconcileAasInputTypes(master, ctx);
            // Before CreateParameterStreams: this declares EyeHeightAsMeters when only a derived
            // scale parameter asked for it, and the stream pass has to see that declaration.
            FeedScaleParameters(master, ctx);
            CreateParameterStreams(master, ctx);
            FeedVelocityMagnitude(master, ctx);
            RehomeVolatileAssets(master, vrcLayers, ctx);
            DeduplicateLayers(master, ctx);
            DropStateMachinelessLayers(master, ctx);
            MaskMergedLayers(master, vrcLayers, ctx);
            // BEFORE the empty-state filler, deliberately. The two passes have mutually exclusive
            // preconditions — this one needs BOTH states to hold clips, the filler needs one to be
            // empty — so running this first makes it impossible for it to re-process the filler's
            // own output. Run the other way round it did exactly that, turning "Toggle Cat Tail
            // restore" into "Toggle Cat Tail restore restore" on 27 avatars.
            RestorePartialOffStates(master, ctx);
            // One shared name registry, so the two restore passes cannot overwrite each other's
            // clips: both write "<thing> restore.anim" into one folder, and a nativized toggle
            // layer and the tree Fury built from that same toggle are named alike often enough.
            var restoreClipPaths = new HashSet<string>();
            FillEmptyStatesWithRestoreClips(master, ctx, restoreClipPaths);
            // AFTER the state pass, which sweeps stale " restore" clips out of the output folder
            // using only ITS OWN keep list — run the other way round, that sweep would delete the
            // tree clips written moments earlier. Both run BEFORE FillEmptyMotionSlots, so the off
            // halves they repair are still genuine holes rather than placeholders.
            FillEmptyTreeSlotsWithRestoreClips(master, ctx, restoreClipPaths);
            // AFTER both fillers, so the clips they wrote are seen and topped only where still
            // missing something. This is the pass that stops relying on Write Defaults at all:
            // ChilloutVR does not restore WD defaults the way VRChat's runtime does, so a binding
            // left to WD is a binding left to nothing.
            AssertOwnedBindingsEverywhere(master, ctx);
            // AFTER the filler, whose motions are what turned this from harmless into a strobe.
            SuppressAnyStateSelfRestarts(master, ctx);
            WarnLocomotionOverrides(vrcLayers, ctx);
            FaceTrackingInjector.Inject(master, ctx);
            AvatarScalerInjector.Inject(master, ctx);
            // After every layer that will exist, exists: the stack order it checks is the one
            // the game will run.
            AuditHandPoseConflicts(master, ctx);
            // Run last: after every merge and injection, make sure no transition conditions
            // it on a parameter using a comparison its final type can't express (e.g. a
            // Float/Bool type-conflict that keeps Float but leaves bool-style If/IfNot
            // conditions behind). ChilloutVR silently drops such transitions.
            ReconcileConditionModes(master, ctx);
            SyncDriverParameterTypes(master, ctx);
            RepairUnconditionalDriverStates(master, ctx);
            VerifyMenuParameterNames(master, ctx);
            PruneDeadMenuEntries(master, ctx);
            WithdrawSelfDrivenExposures(master, ctx);
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
            // AFTER pruning, because pruning decides what is live using the same vestigial-field
            // rule this pass deliberately ignores — run it earlier and the parameters it adds are
            // the first thing thrown away.
            SafeguardBlendParameters(master, ctx);

            // After every merge, injection and clip clone, right before saving: animations that
            // toggled a converted PhysBone's GameObject or component are taught to reach the
            // generated physics as well.
            RewirePhysicsToggles(master, ctx);
            RepairClipPaths(master, ctx);
            AuditClipBindings(master, ctx);
            AuditMaterialProperties(master, ctx);
            ReportKeptFaceTracking(master, ctx);
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
            // AFTER the save, not before: the filler clip is attached with AddObjectToAsset, which
            // needs an object that is already an asset. Called earlier it silently achieved
            // nothing, which is exactly what happened in 3.3.4.
            FillEmptyMotionSlots(ctx, master);
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
            if (animator == null)
            {
                // Was a SILENT skip until 3.5.11, and five avatars in the regression corpus paid
                // for it: Frenni, both Sallys, Stylized Tasque Manager and Tachy shipped prefabs
                // whose Animator still held the controller inherited from the clone source — a
                // VRCFury temp asset Fury deletes on its next build, so the Inspector reads
                // "Missing (Runtime Animator Controller)" — and not one word of it reached the
                // report. An avatar losing its animator is not something to find out by eye.
                ctx.Report.Error(Category, "No Animator found to assign the controller to",
                    "The converted avatar has no Animator component on its root, so the merged " +
                    "controller could not be linked to one. ChilloutVR still loads the avatar — it " +
                    "reads CVRAvatar's own controller fields, which are set — but nothing will " +
                    "animate in the editor, and any Animator left on the object keeps whatever it " +
                    "inherited, which is usually a build-time asset that no longer exists. Add an " +
                    "Animator to the avatar root and convert again.");
            }
            else
            {
                // The override, not the base. ChilloutVR does this itself on load — AssetFilter
                // assigns CVRAvatar.overrides onto the Animator — so pointing at the base here
                // left the editor showing something the game never runs, and play-mode preview
                // disagreeing with the real thing.
                //
                // NOT ASSIGNED AT ALL when the controller references assets that resolve to
                // nothing, because assigning one crashes Unity outright.
                //
                // This took four attempts, and the reason is worth writing down: every earlier fix
                // assumed something that isn't true.
                //
                //   3.0.1  "the asset is still importing"   -> deferred it; crash moved to the delayCall
                //   3.2.0  "a disabled Animator is safe"    -> crash moved to the Inspector
                //   3.3.1  "unlink it after conversion"     -> too late; the assignment already did it
                //
                // The false assumption underneath all three was that a DISABLED Animator stores a
                // controller without building anything. It does not. set_runtimeAnimatorController
                // calls Animator::Rebind regardless of enabled state, and Rebind builds the whole
                // Mecanim playable graph: CreateInternalControllerPlayable -> GenerateGraph ->
                // SetStateMachineInInitialState -> DoBlendTreeEvaluation. A dangling motion
                // reference in there is a segfault, and disabling never had anything to do with it
                // — it only ever looked like it helped because healthy controllers survive.
                //
                // So the check happens BEFORE the assignment, which is the only place it can work.
                // Nothing is lost by skipping it: ChilloutVR reads CVRAvatar.overrides on load, not
                // the Animator, and later conversion passes fall back to ctx.MergedController for
                // the clip list.
                if (ControllerWouldCrashUnity(overrides))
                {
                    ctx.Report.Error(Category, "Controller NOT assigned to the Animator — it crashes Unity",
                        "This controller references assets that resolve to nothing. Handing such a " +
                        "controller to an Animator makes Unity build a playable graph from it on the " +
                        "spot — even with the component switched off — and that walks into the " +
                        "missing references and kills the editor with no error, losing unsaved work. " +
                        "It has been left unassigned so that cannot happen. ChilloutVR is unaffected " +
                        "by that on its own: the CVRAvatar still carries the base controller and the " +
                        "overrides, which is what the client reads on load. The broken references are " +
                        "still in the controller though, so fix them and convert again before " +
                        "uploading — see the unresolvable-asset error for where they came from, " +
                        "usually a VRCFury or Modular Avatar bake that errored partway.");
                }
                else
                {
                    // Cleared first, then set. Not superstition — it is the one thing the
                    // evidence actually supports.
                    //
                    // Sally_PC and Sally_Quest refused this assignment for four versions while
                    // three theories about WHY were wrong in turn. The diagnostic build settled
                    // the facts: the override controller is persisted and valid, its base
                    // resolves, the Animator is enabled on an active object, and the component is
                    // on neither a prefab asset nor a prefab instance — so both earlier "fixes"
                    // were no-ops on this avatar. The assignment simply produced null.
                    //
                    // What those two have that the working avatars do not: no VRCFury. Everything
                    // that converts correctly here is baked by Fury, which builds its own target;
                    // without it the target is an Object.Instantiate clone, and the clone inherits
                    // the source's Animator complete with its DEAD controller reference. Writing
                    // null was already known to stick — the saved prefab came out {fileID: 0} —
                    // while overwriting the dead reference in place did not, which is a component
                    // whose native rebind failed and will not take a new controller until the
                    // broken one is let go of.
                    animator.runtimeAnimatorController = null;
                    animator.runtimeAnimatorController = overrides;

                    // Register the change as a prefab-instance override, or it does not survive.
                    //
                    // Avatars are almost always prefab INSTANCES, and a plain property set on an
                    // instance is not automatically recorded as an override. SaveConvertedPrefab
                    // then calls SaveAsPrefabAssetAndConnect, which reconnects the object and
                    // reverts anything unrecorded to the prefab's own value — so the controller
                    // this line just assigned quietly went back to whatever the source prefab
                    // had.
                    //
                    // Invisible on most avatars, because the value it reverts TO is a live
                    // controller and everything looks fine. Sally_PC and Sally_Quest are the
                    // case where it is not: their source prefabs' Animator override points at a
                    // controller that no longer exists, so the revert handed the conversion a
                    // dangling reference. Sally_PC_SPS, same avatar, healthy source value,
                    // reverted just as hard and nobody could tell.
                    if (PrefabUtility.IsPartOfPrefabInstance(animator))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                    }

                    // Read it back. This is not paranoia: Sally_PC and Sally_Quest reached this
                    // line, ran it, and still shipped prefabs whose Animator held the SOURCE
                    // avatar's controller — a GUID that resolves to nothing, inherited through the
                    // clone from a scene where it was already dead. The assignment did not take,
                    // nothing threw, and no error reached the report, so the avatar simply arrived
                    // without an animator and the only way anyone found out was by clicking on it.
                    //
                    // A dangling reference is worse than an empty one, too: it reads as null to
                    // script while the serialized GUID survives into the prefab, so it looks fine
                    // to every check that asks the object what it has. If it would not stick, it
                    // gets cleared — an empty slot is honest and cannot crash the graph builder —
                    // and the report says so.
                    if (animator.runtimeAnimatorController != overrides)
                    {
                        // The Inspector's path, not the API's. Five attempts went through the
                        // C# setter — plain, recorded as a prefab override, after an unpack,
                        // cleared-then-set, and after a forced synchronous import — and on two
                        // avatars every one of them silently stored null while the very same
                        // asset dragged into the very same slot BY HAND worked at once. The
                        // Inspector does not call the setter: dragging writes the serialized
                        // m_Controller property through a SerializedObject. The maintainer
                        // proved that path works on these exact avatars; this does the same
                        // thing programmatically.
                        //
                        // It is also consistent with every measurement: the serialized field
                        // held the source's dead GUID for weeks and holds our null happily —
                        // serialization never refused anything. Whatever rejects the controller
                        // lives in the native setter, so the repair simply does not go through
                        // it. The prefab is saved from serialized data, and the native side
                        // binds from it on the next load, exactly as it does after a manual
                        // drag.
                        var serialized = new SerializedObject(animator);
                        serialized.FindProperty("m_Controller").objectReferenceValue = overrides;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        if (PrefabUtility.IsPartOfPrefabInstance(animator))
                        {
                            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                        }

                        // And make the native side notice. The serialized write alone leaves the
                        // component in a half-state for the rest of the session: the Inspector
                        // slot shows the controller, the prefab saves it, and the Animator's
                        // native binding still holds nothing — Clip Count: 0, no preview, until
                        // a scene reload rebinds from serialized data. Rebind() forces that
                        // rebuild now. Safe here for the same reason the assignment was: this
                        // path only runs on a controller the crash guard already cleared.
                        animator.Rebind();
                        ctx.Report.Approximated(Category,
                            "Controller linked through the serialized property",
                            "Unity's Animator API refused this assignment (it stores null, " +
                            "silently, for reasons it does not report), so the reference was " +
                            "written the way the Inspector writes it instead, and the Animator " +
                            "rebound to pick it up. The saved prefab carries the correct " +
                            "controller either way.");
                    }

                    // Judged on the SERIALIZED value, not the getter. The getter answers from
                    // the native binding, which can lag a serialized write within the same
                    // editor frame — and the serialized value is the one that reaches the
                    // prefab and the one ChilloutVR loads.
                    var verify = new SerializedObject(animator);
                    var heldReference = verify.FindProperty("m_Controller").objectReferenceValue;
                    if (heldReference != overrides)
                    {
                        // What the read-back actually saw. Three mechanism theories have now been
                        // wrong about this — the prefab-override revert, then the prefab
                        // connection, then a dangling sub-asset reference — each plausible, each
                        // disproved only after a build and a run. The check knows the answer at
                        // the moment it fails and was throwing it away; the next failure carries
                        // it into the report instead.
                        var getterHeld = animator.runtimeAnimatorController;
                        string evidence =
                            $"[serialized={(heldReference == null ? "null" : heldReference.name)}" +
                            $"; getter={(getterHeld == null ? "null" : getterHeld.name + " (" + getterHeld.GetType().Name + ")")}" +
                            $"; wanted={(overrides == null ? "null" : overrides.name)}" +
                            $"; wantedPath=\"{AssetDatabase.GetAssetPath(overrides)}\"" +
                            $"; wantedPersisted={EditorUtility.IsPersistent(overrides)}" +
                            $"; animatorOnPrefabAsset={PrefabUtility.IsPartOfPrefabAsset(animator)}" +
                            $"; animatorOnPrefabInstance={PrefabUtility.IsPartOfPrefabInstance(animator)}" +
                            $"; animatorEnabled={animator.enabled}" +
                            $"; objectActive={animator.gameObject.activeInHierarchy}] ";

                        // Cleared through the same serialized path the repair used — the API
                        // setter has proven it cannot be trusted on this component.
                        verify.FindProperty("m_Controller").objectReferenceValue = null;
                        verify.ApplyModifiedPropertiesWithoutUndo();
                        ctx.Report.Error(Category, "Controller would not stay assigned to the Animator",
                            evidence +
                            "The merged controller was assigned and did not stick — the Animator kept " +
                            "a reference of its own instead, usually one inherited from the source " +
                            "avatar that already pointed at a deleted asset. The slot has been cleared " +
                            "rather than left holding a dead reference, which reads as empty to scripts " +
                            "while still being serialized into the prefab. ChilloutVR is unaffected: it " +
                            "reads CVRAvatar's controller fields on load, and those are set correctly. " +
                            "Check the SOURCE avatar's Animator — if its controller shows as Missing " +
                            "there, fix it there and convert again.");
                    }
                }
            }
            EditorUtility.SetDirty(ctx.CvrAvatar);
        }

        /// <summary>
        /// Gives every empty blend-tree slot a real (empty) clip, because an empty one crashes Unity.
        ///
        /// A blend tree child whose motion is missing — the asset it named is gone, or a bake never
        /// produced it — is the thing at the bottom of every crash dump this project has collected:
        /// <c>DoBlendTreeEvaluation</c>, reached from <c>GenerateGraph</c>. It fires whenever
        /// ANYTHING builds a playable graph from the controller, and plenty of things do that are
        /// nothing to do with this tool: assigning it to an Animator, enabling one, selecting the
        /// object in the Inspector, and — the one that matters most — the CCK's own uploader, which
        /// instantiates the avatar to build it and takes the editor down with it. An avatar in that
        /// state simply cannot be uploaded.
        ///
        /// Refusing to assign the controller only protected our own step. This removes the hazard
        /// itself: each empty slot gets a shared filler clip. Nothing is lost — the slot already
        /// animated nothing — but the graph builder now has a valid motion to read instead of a
        /// hole, so the controller is safe for anyone to instantiate.
        ///
        /// THE FILLER IS NOT AN EMPTY CLIP, which is what 3.4.2 through 3.4.8 used. A clip with no
        /// curves at all is degenerate to Mecanim, and binding one trips
        ///
        ///     Assertion failed on expression: 'mem->m_ConstantClipValueCount >= 0 &&
        ///     mem->m_ConstantClipValueCount &lt;= (int)clip->m_ConstantClip.curveCount'
        ///
        /// once per state that holds it — the count it is comparing is the size of the value array
        /// the animator reads and writes bindings through.
        ///
        /// BE PRECISE ABOUT WHAT THIS FIXED, because the avatar it was found on had two things
        /// wrong at once. The assertions, a menu whose controls had swapped places, and a body in
        /// the wrong materials all came from Unity's "Enter Play Mode Options" being on — proven by
        /// turning it off and watching every symptom go, with no reconversion. See
        /// BridgeConverter.WarnFastPlayMode. What a curve-less placeholder does is make OUR output
        /// the thing that setting breaks, on an avatar that would otherwise survive it, and 66
        /// states shared one on that avatar.
        ///
        /// So the filler carries ONE constant curve, on a dedicated empty child of the avatar that
        /// nothing else touches, holding that object's own active state at the value it already
        /// has. Writing it changes nothing on any frame; existing means Mecanim has a real curve
        /// count to agree with itself about, and a user who likes fast play mode keeps it.
        ///
        /// Deliberately NOT deleting the children. Blend tree children carry thresholds, and
        /// removing one re-numbers its neighbours and changes how the rest blend. A filler clip in
        /// place keeps every threshold where the author put it.
        /// </summary>
        static void FillEmptyMotionSlots(BridgeContext ctx, AnimatorController master)
        {
            AnimationClip filler = null;
            int filled = 0;
            int states = 0;
            var seen = new HashSet<Motion>();

            AnimationClip Filler()
            {
                if (filler == null)
                {
                    filler = new AnimationClip { name = "AvatarBridge_EmptySlot" };
                    string anchor = EmptySlotAnchorPath(ctx);
                    if (anchor != null)
                    {
                        // One curve, constant, asserting a value the object already has. See the
                        // summary: a curve-less clip misaligns the animator's binding array.
                        filler.SetCurve(anchor, typeof(GameObject), "m_IsActive",
                            AnimationCurve.Constant(0f, 1f / 60f, 1f));
                    }
                    AssetDatabase.AddObjectToAsset(filler, master);
                }
                return filler;
            }

            void Walk(Motion motion)
            {
                if (!(motion is BlendTree tree) || !seen.Add(tree))
                {
                    return;
                }
                var children = tree.children;
                bool changed = false;
                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i].motion == null || IsCurveless(children[i].motion, filler))
                    {
                        children[i].motion = Filler();
                        changed = true;
                        filled++;
                    }
                    else
                    {
                        Walk(children[i].motion);
                    }
                }
                if (changed)
                {
                    // children is a copy; the setter is what writes it back.
                    tree.children = children;
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        // The state's OWN motion as well as anything nested below it. An empty
                        // state was the case 3.3.4 missed entirely — it only ever looked at blend
                        // tree children, so it found nothing to do on the avatar that crashed and
                        // said nothing. Unity works out a state's duration from its motion, and
                        // does that while building the graph (EvaluateStateDuration, under
                        // SetStateMachineInInitialState), so a state with no motion is the same
                        // hole as an empty blend tree slot.
                        if (child.state.motion == null || IsCurveless(child.state.motion, filler))
                        {
                            child.state.motion = Filler();
                            filled++;
                            states++;
                        }
                        else
                        {
                            Walk(child.state.motion);
                        }
                    }
                });
            }

            if (filled > 0)
            {
                EditorUtility.SetDirty(master);
                AssetDatabase.SaveAssets();
            }

            if (filled > 0)
            {
                ctx.Report.Warning(Category,
                    $"{filled} empty motion slot(s) given a placeholder clip" +
                    (states == 0 ? " (all blend tree slots)"
                        : states == filled ? " (all animator states)"
                        : $" ({states} animator states, {filled - states} blend tree slots)"),
                    "These slots had no motion, or held a clip with no curves in it — an asset " +
                    "that's gone, one the avatar's own build step never produced, or a state left " +
                    "empty. Unity CRASHES when it builds a playable graph containing an empty slot, " +
                    "which happens when the controller is assigned to an Animator, when you select " +
                    "the avatar, and when the CCK builds it to upload — so this had to be repaired " +
                    "rather than reported. A CURVE-LESS clip is just as bad in a quieter way: Mecanim " +
                    "sizes its binding array from the curve count, and a clip with none misaligns it, " +
                    "so bindings land on each other's slots — the symptom is menu controls that have " +
                    "swapped places and materials selecting the wrong option, with " +
                    "\"Assertion failed on expression: 'mem->m_ConstantClipValueCount ...'\" in the " +
                    "console. Each slot now holds a placeholder that animates one inert value on the " +
                    "avatar's \"AvatarBridge_EmptySlot\" object, so it changes nothing and Mecanim " +
                    "still has a curve to count. Every threshold stays where the author put it. " +
                    "Whatever those motions were supposed to be is still missing, so find out why " +
                    "they didn't arrive before you rely on the feature that used them.");
            }
        }

        /// <summary>
        /// A clip that animates literally nothing, which Mecanim cannot size a binding array from.
        ///
        /// Humanoid clips are excluded deliberately: their muscle data lives outside the curve
        /// arrays, so VRChat's <c>proxy_hands_*</c> and every FBX animation read as curve-less here
        /// and are perfectly valid. Legacy clips likewise never reach a Mecanim graph.
        /// </summary>
        static bool IsCurveless(Motion motion, AnimationClip filler)
        {
            if (!(motion is AnimationClip clip) || clip == filler)
            {
                return false;
            }
            return !clip.humanMotion && !clip.legacy
                && AnimationUtility.GetCurveBindings(clip).Length == 0
                && AnimationUtility.GetObjectReferenceCurveBindings(clip).Length == 0;
        }

        /// <summary>
        /// A dedicated empty child of the avatar, existing only so the placeholder clip has
        /// something inert to animate. Nothing else reads it, nothing else writes it, and holding
        /// its own active state at the value it already has is a no-op on every frame — the point
        /// is purely that the clip carries a curve at all.
        /// </summary>
        static string EmptySlotAnchorPath(BridgeContext ctx)
        {
            if (ctx == null || ctx.Target == null)
            {
                return null;
            }
            const string anchorName = "AvatarBridge_EmptySlot";
            var anchor = ctx.Target.transform.Find(anchorName);
            if (anchor == null)
            {
                var holder = new GameObject(anchorName);
                holder.transform.SetParent(ctx.Target.transform, false);
                anchor = holder.transform;
            }
            anchor.gameObject.SetActive(true);
            return ctx.PathInTarget(anchor);
        }

        /// <summary>
        /// Whether a controller asset (or anything it wraps) references a GUID that resolves to no
        /// asset in this project.
        ///
        /// Read from the saved FILE rather than the object graph, because that is where a dangling
        /// reference is still visible: in managed code it has already collapsed to a plain null,
        /// indistinguishable from a state that legitimately has no motion. Cheap enough to run
        /// once — a text scan of one .controller.
        /// </summary>
        /// <summary>
        /// The GUIDs a serialized file genuinely REFERENCES, as opposed to ones that merely
        /// appear somewhere in its text.
        ///
        /// Unity writes an external object reference as the whole triple
        /// <c>{fileID: N, guid: G, type: N}</c>, and nothing else takes that shape, so matching
        /// the triple is exact. Scanning for a bare "guid: &lt;32 hex&gt;" is not — and the
        /// difference is not academic. Unity names a broken prefab instance
        /// <c>SFX (Missing Prefab with guid: ea09b303…)</c>, that NAME then appears in every
        /// animation curve path and avatar mask entry targeting the object, and a bare scan reads
        /// it as a reference to a deleted asset.
        ///
        /// It cost two avatars their entire animator controller. BHFBunny and Sultry Snake each
        /// carry one such placeholder under an SPS socket; the crash guard below read the name out
        /// of a curve path, concluded the controller pointed at something deleted, and refused to
        /// assign it — leaving a converted avatar with no controller at all. On Sultry Snake the
        /// bare scan found 613 GUIDs where only 599 were references.
        ///
        /// Narrowing the match does not weaken the guard: a genuinely missing asset is still
        /// written as the full triple, so it still matches.
        /// </summary>
        internal static IEnumerable<string> ReferencedGuids(string yaml)
        {
            foreach (Match match in Regex.Matches(
                         yaml, @"\{fileID:\s*-?\d+,\s*guid:\s*([0-9a-f]{32}),\s*type:\s*-?\d+\}"))
            {
                yield return match.Groups[1].Value;
            }
        }

        internal static bool ControllerWouldCrashUnity(RuntimeAnimatorController controller)
        {
            try
            {
                var paths = new List<string>();
                for (RuntimeAnimatorController c = controller; c != null; )
                {
                    string p = AssetDatabase.GetAssetPath(c);
                    if (!string.IsNullOrEmpty(p))
                    {
                        paths.Add(p);
                    }
                    c = c is AnimatorOverrideController over ? over.runtimeAnimatorController : null;
                }
                foreach (string assetPath in paths)
                {
                    string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
                    if (!File.Exists(absolute))
                    {
                        continue;
                    }
                    foreach (string guid in ReferencedGuids(File.ReadAllText(absolute)))
                    {
                        if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Unreadable for any reason: treat as unsafe. Leaving an Animator switched off is
                // an inconvenience; a hard editor crash costs unsaved work.
                return true;
            }
            return false;
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

            if (DroppedPlayAudioCount > 0)
            {
                ctx.Report.Skipped(Category,
                    $"{DroppedPlayAudioCount} animator-driven audio player(s) removed",
                    string.Join("; ", DroppedPlayAudio) + (DroppedPlayAudioCount > DroppedPlayAudio.Count ? "; …" : "") +
                    " — VRChat plays these from the animator state itself (music toggles, sound " +
                    "effects), and ChilloutVR has no equivalent state behaviour. The AudioSource " +
                    "each one pointed at is still on the avatar, so the sound can be wired by " +
                    "hand: a toggle animating the AudioSource's enabled flag with Play On Awake " +
                    "set plays and stops it, and ChilloutVR's own CVRAudioDriver can switch " +
                    "between clips from an animated index.");
            }
            if (DroppedPoseSpaceCount > 0)
            {
                ctx.Report.Skipped(Category,
                    $"{DroppedPoseSpaceCount} viewpoint shift(s) during poses removed",
                    string.Join(", ", DroppedPoseSpace) +
                    " — VRChat moved the wearer's viewpoint (usually down to the hips) while " +
                    "these poses played, so crawling didn't leave the camera at standing height. " +
                    "ChilloutVR has no hook for that: the pose still plays, the camera stays on " +
                    "the head.");
            }
        }

        /// <summary>Inventory of dropped VRCAnimatorPlayAudio behaviours, reset per Run.</summary>
        static readonly List<string> DroppedPlayAudio = new List<string>();
        static int DroppedPlayAudioCount;
        static readonly List<string> DroppedPoseSpace = new List<string>();
        static int DroppedPoseSpaceCount;

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
                else if (behaviour is VRC.SDK3.Avatars.Components.VRCAnimatorPlayAudio playAudio)
                {
                    // Animator-driven audio — music toggles, SFX on states; 86 across the wild
                    // census, so it deserves its own inventory instead of a bare type count. No
                    // conversion yet: the honest play/stop approximation (AudioSource enable
                    // window) interacts with Write Defaults — leaving the state stops the WRITES,
                    // not the audio — so it needs the off-state restore machinery and its own
                    // design. The AudioSource itself survives conversion untouched.
                    if (DroppedPlayAudio.Count < 6)
                    {
                        string clips = playAudio.Clips == null ? "no clips"
                            : string.Join(", ", playAudio.Clips.Where(c => c != null).Select(c => c.name).Take(3));
                        DroppedPlayAudio.Add(
                            $"state \"{state?.name ?? "(machine)"}\" ← \"{playAudio.SourcePath}\" ({clips}"
                            + (playAudio.Loop ? ", looping" : "") + ")");
                    }
                    DroppedPlayAudioCount++;
                    UnityEngine.Object.DestroyImmediate(behaviour, true);
                }
                else if (behaviour.GetType().Name == "VRCAnimatorTemporaryPoseSpace")
                {
                    if (DroppedPoseSpace.Count < 4 && state != null)
                    {
                        DroppedPoseSpace.Add(state.name);
                    }
                    DroppedPoseSpaceCount++;
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
                        "and locomotion only, and that is the platform's own limit rather than a gap in this " +
                        "conversion: the CCK declares the mask list with the comment \"TODO: Add FingerTracking " +
                        "masks when GS is ready\". There is nothing to map these onto yet." +
                        (DroppedFingers > 0
                            ? " FINGERS ARE THE ONE TO WATCH. In VRChat an emote sets them to \"Animation\" so the " +
                              "emote's own hand pose plays instead of the gesture you are holding, and VRChat's " +
                              "Action layer sits ABOVE its Gesture layer, so it wins twice over. ChilloutVR's " +
                              "layer order is the other way round — emotes are grafted into Locomotion/Emotes, " +
                              "which sits BELOW the hand-pose layers — so expect an emote's hand pose to be " +
                              "overridden by whatever gesture your controller is reporting. If a dance looks " +
                              "right except that the hands hold a fist or a point, this is why. The workaround " +
                              "today is to hold an open/neutral gesture while the emote plays."
                            : "") +
                        " Eyes and mouth keep whatever the avatar's own animation and face tracking do with them, " +
                        "which is usually what you want — face tracking wants those channels anyway.");
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
                // Already-local names are left alone — the Action transplant declares its
                // "#AB_Ready" flag and scratch cells BEFORE this pass runs, and prefixing them
                // again made "##AB_Ready_…": still local, still consistent, but a name no reader
                // of the tester or the report should ever have to puzzle over.
                if (ctx.Settings.preserveParameterSyncState && !preserved
                    && !result.StartsWith("#", StringComparison.Ordinal))
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
        /// <summary>
        /// Removes layers that have no state machine at all.
        ///
        /// A layer can arrive this way when its state machine lived in a DIFFERENT asset. Unity
        /// stores <c>m_StateMachine</c> as a cross-file reference, so copying a layer between
        /// controllers keeps it pointing at the original file — and when that file is stripped
        /// (or simply absent), the layer survives with nothing behind it.
        ///
        /// Found on an avatar whose "Flying" and "Flying Scale" layers borrowed their state
        /// machines from GoGo Loco's own controller. Removing GoGo took the machines with it and
        /// left two husks, which Unity complains about on EVERY evaluation: a tester's play-mode
        /// log carried 534 "Statemachine for layer is missing" lines per avatar. The layer cannot
        /// do anything without a state machine, so nothing is lost by dropping it — and it is
        /// dropped here, late, rather than trusted to the strip pass, because the reference can
        /// break for reasons that have nothing to do with stripping.
        /// </summary>
        static void DropStateMachinelessLayers(AnimatorController master, BridgeContext ctx)
        {
            var layers = master.layers;
            var kept = layers.Where(l => l != null && l.stateMachine != null).ToArray();
            if (kept.Length == layers.Length)
            {
                return;
            }
            var dropped = layers.Where(l => l == null || l.stateMachine == null)
                                .Select(l => l == null ? "<null layer>" : $"\"{l.name}\"")
                                .ToList();
            master.layers = kept;
            EditorUtility.SetDirty(master);
            ctx.Report.Converted(Category,
                $"Removed {dropped.Count} animator layer(s) with no state machine",
                string.Join(", ", dropped) + " — these layers had nothing behind them. Unity keeps " +
                "a layer's state machine as a reference to an asset, which can point at a DIFFERENT " +
                "controller when the layer was copied between avatars; if that controller is " +
                "stripped or missing, the layer is left empty. An empty layer can never play " +
                "anything, and Unity logs \"Statemachine for layer is missing\" every time it " +
                "evaluates one — hundreds of lines per second in play mode. Nothing is lost by " +
                "removing them, but if a feature you expected is gone, this names the layer it " +
                "would have been in.");
        }

        /// <summary>
        /// Tops up an "off" state that restores SOME of what its sibling animates, but not all.
        ///
        /// FillEmptyStatesWithRestoreClips only considers states with no motion at all, and stays
        /// that way deliberately — its comment records two bugs from being more eager. But an off
        /// state can under-restore while still holding a clip, and then it is invisible to that
        /// pass: on the avatar that found this, a two-state contact layer's "Stop" clip toggled an
        /// AudioSource off and nothing else, while its "Play" clip ALSO rotated a bone. With Write
        /// Defaults off, every trigger left that rotation exactly where the clip stopped, and each
        /// retrigger stacked on the last — a bone that drifted further from rest every time it was
        /// touched, with nothing able to put it back.
        ///
        /// The rule here is narrower than the empty-state one rather than looser, which is what
        /// makes it safe: the layer must have exactly two states, BOTH must hold clips, and one
        /// clip's bindings must be a STRICT SUBSET of the other's. That combination is positive
        /// evidence of an off state — it already turns something off — so the only question is
        /// what it forgot. Only the forgotten bindings are added; its own curves are copied over
        /// untouched.
        /// </summary>
        static void RestorePartialOffStates(AnimatorController master, BridgeContext ctx)
        {
            var topped = new List<string>();
            int curvesAdded = 0;

            // Which layer owns each property. A binding two layers animate belongs to NEITHER for
            // restoring purposes: if both restore it they fight, and the loser's toggle stops
            // working. FillEmptyStatesWithRestoreClips applies the same rule, which is why its
            // restore clips legitimately omit shared bindings — without this, the pass below saw
            // those omissions as an off state "forgetting" them and topped the clip back up,
            // producing "Toggle Cat Tail restore restore" and undoing the arbitration.
            var owner = new Dictionary<EditorCurveBinding, int>();
            var contested = new HashSet<EditorCurveBinding>();
            for (int i = 0; i < master.layers.Length; i++)
            {
                var l = master.layers[i];
                if (l?.stateMachine == null) continue;
                var here = new HashSet<EditorCurveBinding>();
                // Same reachability rule as BuildRestoreOwnership: a library layer's orphan
                // states can never play, so they own nothing — see LibraryDefaultState.
                var onlyPlayable = LibraryDefaultState(l);
                WalkMachines(l.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        if (child.state?.motion is AnimationClip c
                            && (onlyPlayable == null || child.state == onlyPlayable))
                        {
                            foreach (var binding in AnimationUtility.GetCurveBindings(c)) here.Add(binding);
                        }
                    }
                });
                foreach (var binding in here)
                {
                    if (owner.TryGetValue(binding, out int first) && first != i) contested.Add(binding);
                    else owner[binding] = i;
                }
            }

            for (int layerIndex = 0; layerIndex < master.layers.Length; layerIndex++)
            {
                var layer = master.layers[layerIndex];
                if (layer?.stateMachine == null || IsProtectedLayer(layer.name))
                {
                    continue;
                }

                var states = new List<AnimatorState>();
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        if (child.state != null) states.Add(child.state);
                    }
                });
                if (states.Count != 2)
                {
                    continue;   // anything larger is a machine, not a toggle — same reasoning as above
                }
                if (!(states[0].motion is AnimationClip a) || !(states[1].motion is AnimationClip b))
                {
                    continue;   // an empty half is the other pass's job
                }

                var setA = new HashSet<EditorCurveBinding>(AnimationUtility.GetCurveBindings(a));
                var setB = new HashSet<EditorCurveBinding>(AnimationUtility.GetCurveBindings(b));

                AnimatorState offState; AnimationClip offClip; HashSet<EditorCurveBinding> missing;
                if (setA.Count > setB.Count && setB.IsProperSubsetOf(setA))
                {
                    offState = states[1]; offClip = b; missing = new HashSet<EditorCurveBinding>(setA); missing.ExceptWith(setB);
                }
                else if (setB.Count > setA.Count && setA.IsProperSubsetOf(setB))
                {
                    offState = states[0]; offClip = a; missing = new HashSet<EditorCurveBinding>(setB); missing.ExceptWith(setA);
                }
                else
                {
                    continue;   // neither is a subset: two different jobs, not on/off
                }

                var filled = new AnimationClip { name = SanitizeFileName($"{offClip.name} restore") };
                foreach (var binding in AnimationUtility.GetCurveBindings(offClip))
                {
                    AnimationUtility.SetEditorCurve(filled, binding, AnimationUtility.GetEditorCurve(offClip, binding));
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(offClip))
                {
                    AnimationUtility.SetObjectReferenceCurve(filled, binding,
                        AnimationUtility.GetObjectReferenceCurve(offClip, binding));
                }

                int added = 0;
                foreach (var binding in missing)
                {
                    if (binding.type == typeof(Animator))
                    {
                        continue;   // parameters, not properties — nothing to restore on the avatar
                    }
                    if (contested.Contains(binding) || !owner.TryGetValue(binding, out int owns)
                        || owns != layerIndex)
                    {
                        continue;   // another layer animates it too — arbitration belongs to them
                    }
                    if (!AnimationUtility.GetFloatValue(ctx.Target, binding, out float value))
                    {
                        continue;   // property not present on this avatar; leave it alone
                    }
                    AnimationUtility.SetEditorCurve(filled, binding, AnimationCurve.Constant(0f, 0f, value));
                    added++;
                }
                if (added == 0)
                {
                    continue;
                }

                // Same home and claiming rule as every other generated clip, so reconversion
                // replaces it instead of stacking "restore 2", "restore 3", ...
                string target = OutputAssetPaths.Claim(
                    $"{ctx.OutputDir}/RehomedAssets/{SanitizeFileName(filled.name)}.anim");
                var folder = System.IO.Path.GetDirectoryName(target).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    System.IO.Directory.CreateDirectory(folder);
                    AssetDatabase.Refresh();
                }
                // Delete anything already at the path FIRST, exactly as the empty-state filler
                // does. CreateAsset over an existing asset replaces the object, and every other
                // state still referencing the old one is left with a null motion — which the
                // placeholder pass then papers over with an empty clip. On one avatar that cost
                // four states their animation, one per layer this pass touched.
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(target) != null)
                {
                    AssetDatabase.DeleteAsset(target);
                }
                AssetDatabase.CreateAsset(filled, target);
                offState.motion = filled;
                EditorUtility.SetDirty(offState);
                curvesAdded += added;
                topped.Add($"\"{layer.name}\" ({added})");
            }

            if (topped.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{topped.Count} \"off\" state(s) topped up to restore everything their layer animates",
                    string.Join(", ", topped) + $" — {curvesAdded} propert(ies) in total. These states " +
                    "already switched something off, but their clip left out properties the layer's OTHER " +
                    "state animates. With Write Defaults off nothing puts those back, so each trigger left " +
                    "them wherever the animation stopped and the next one stacked on top — a bone that " +
                    "drifts further from rest every time the control is used. Each off state now also holds " +
                    "the value the property has on this avatar right now, so it returns properly. If one of " +
                    "these should rest somewhere else, set that up before converting: whatever is true at " +
                    "conversion time is what \"off\" now means.");
            }
        }

        /// <summary>
        /// Every property a layer OWNS is asserted from every state that layer can rest in.
        ///
        /// ChilloutVR does not fall back to Write Defaults the way VRChat's runtime does —
        /// measured in game, twice, on one avatar: every toggle whose "on" direction was an
        /// empty state switched off and never back on, while toggles whose states assert their
        /// properties kept working. So the owner arbitration ("the lowest layer animating a
        /// property restores it; higher layers stay silent") only functions if the owner
        /// actually SPEAKS from every state it can rest in. Before this pass it spoke only from
        /// clips that happened to mention the property — an exclusive-wear wardrobe, where one
        /// outfit's clip also hides the other garments, left most owners silent at rest and the
        /// whole wardrobe one-way in game.
        ///
        /// Deliberately left alone, each for a reason that has already shipped as a bug or been
        /// measured as a fight:
        ///   * bindings that appear inside any blend tree in the SAME layer — a slider owns its
        ///     value, and pinning it from a plain state reverts the slider whenever the layer
        ///     rests (the Reset state that flattened seven chest blendshapes);
        ///   * states with no clip at all — a 2-state toggle's empty half is the empty-state
        ///     filler's job, and anything larger left empty is structural;
        ///   * pass-through states — a local/remote gate is never rested in;
        ///   * Animator-typed bindings — muscles and animated parameters are not scene state;
        ///   * unreachable library states, protected layers, and states holding blend trees.
        /// </summary>
        static void AssertOwnedBindingsEverywhere(AnimatorController master, BridgeContext ctx)
        {
            BuildRestoreOwnership(master.layers, out var owner, out _, out var treeDriven);

            int curvesAddedTotal = 0;
            var touched = new List<string>();

            for (int layerIndex = 0; layerIndex < master.layers.Length; layerIndex++)
            {
                var layer = master.layers[layerIndex];
                if (layer?.stateMachine == null || IsProtectedLayer(layer.name))
                {
                    continue;
                }
                var onlyPlayable = LibraryDefaultState(layer);

                // What this layer animates from plain clip states. Tree-driven bindings are
                // exempt globally — ownership never contains them (see BuildRestoreOwnership).
                var stateFloats = new HashSet<EditorCurveBinding>();
                var stateObjects = new HashSet<EditorCurveBinding>();
                var states = new List<AnimatorState>();
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        if (state == null || (onlyPlayable != null && state != onlyPlayable))
                        {
                            continue;
                        }
                        states.Add(state);
                        if (state.motion is AnimationClip clip)
                        {
                            foreach (var b in AnimationUtility.GetCurveBindings(clip)) stateFloats.Add(b);
                            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip)) stateObjects.Add(b);
                        }
                    }
                });

                var ownedFloats = stateFloats.Where(b => b.type != typeof(Animator)
                    && !treeDriven.Contains(b)
                    && owner.TryGetValue(b, out int by) && by == layerIndex).ToList();
                var ownedObjects = stateObjects.Where(b => !treeDriven.Contains(b)
                    && owner.TryGetValue(b, out int by) && by == layerIndex).ToList();
                if (ownedFloats.Count == 0 && ownedObjects.Count == 0)
                {
                    continue;
                }

                int addedThisLayer = 0;
                foreach (var state in states)
                {
                    if (!(state.motion is AnimationClip current) || IsPassThroughState(state))
                    {
                        continue;
                    }
                    var haveFloats = new HashSet<EditorCurveBinding>(AnimationUtility.GetCurveBindings(current));
                    var haveObjects = new HashSet<EditorCurveBinding>(AnimationUtility.GetObjectReferenceCurveBindings(current));

                    AnimationClip copy = null;
                    int added = 0;
                    AnimationClip Copy()
                    {
                        if (copy != null)
                        {
                            return copy;
                        }
                        copy = new AnimationClip
                        {
                            name = SanitizeFileName($"{layer.name} {state.name} restore")
                        };
                        foreach (var b in haveFloats)
                        {
                            AnimationUtility.SetEditorCurve(copy, b, AnimationUtility.GetEditorCurve(current, b));
                        }
                        foreach (var b in haveObjects)
                        {
                            AnimationUtility.SetObjectReferenceCurve(copy, b,
                                AnimationUtility.GetObjectReferenceCurve(current, b));
                        }
                        return copy;
                    }

                    foreach (var binding in ownedFloats)
                    {
                        if (haveFloats.Contains(binding)
                            || !AnimationUtility.GetFloatValue(ctx.Target, binding, out float value))
                        {
                            continue;
                        }
                        AnimationUtility.SetEditorCurve(Copy(), binding, AnimationCurve.Constant(0f, 0f, value));
                        added++;
                    }
                    foreach (var binding in ownedObjects)
                    {
                        if (haveObjects.Contains(binding)
                            || !AnimationUtility.GetObjectReferenceValue(ctx.Target, binding, out var value))
                        {
                            continue;
                        }
                        AnimationUtility.SetObjectReferenceCurve(Copy(), binding,
                            new[] { new ObjectReferenceKeyframe { time = 0f, value = value } });
                        added++;
                    }
                    if (added == 0)
                    {
                        continue;
                    }

                    // Same home, claiming and delete-first rules as the other restore writers, so
                    // reconversion replaces cleanly instead of stacking numbered copies.
                    string target = OutputAssetPaths.Claim(
                        $"{ctx.OutputDir}/RehomedAssets/{copy.name}.anim");
                    var folder = System.IO.Path.GetDirectoryName(target).Replace('\\', '/');
                    if (!AssetDatabase.IsValidFolder(folder))
                    {
                        System.IO.Directory.CreateDirectory(folder);
                        AssetDatabase.Refresh();
                    }
                    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(target) != null)
                    {
                        AssetDatabase.DeleteAsset(target);
                    }
                    AssetDatabase.CreateAsset(copy, target);
                    state.motion = copy;
                    EditorUtility.SetDirty(state);
                    addedThisLayer += added;
                }

                if (addedThisLayer > 0)
                {
                    curvesAddedTotal += addedThisLayer;
                    touched.Add($"\"{layer.name}\" ({addedThisLayer})");
                }
            }

            if (touched.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{touched.Count} layer(s) now assert everything they own, from every state",
                    $"{string.Join(", ", touched.Take(8))}{(touched.Count > 8 ? ", …" : "")} — " +
                    $"{curvesAddedTotal} propert(ies) in total. VRChat quietly puts a property back " +
                    "to its default when no animation is writing it (Write Defaults); ChilloutVR " +
                    "does not, so anything left to that rule switches off and never back on in " +
                    "game. Each layer that owns a property now states its value from every state " +
                    "it can rest in, so nothing is ever left to the runtime's discretion. Whatever " +
                    "is true at conversion time is what those values are — set the avatar up the " +
                    "way it should rest before converting.");
            }

            ReportNoWdCoverage(master, ctx);
        }

        /// <summary>
        /// The audit for everything above: after every restore pass has run, is any owned
        /// property still able to fall back to the runtime?
        ///
        /// This exists because the chain it checks was broken four separate ways on one avatar
        /// — curveless states invisible to the filler, a dead library owning the wardrobe, tree
        /// claims orphaning bindings, owners silent at rest — and each was found by hand, from a
        /// game report, days apart. The checker asks the finished controller the one question
        /// all of those reduce to, with the same helpers the passes themselves use, so it cannot
        /// drift from them. A violation here lands in the report as a warning, which the
        /// regression corpus records in full — so this entire class of bug now fails loudly at
        /// conversion time instead of quietly in game.
        /// </summary>
        static void ReportNoWdCoverage(AnimatorController master, BridgeContext ctx)
        {
            BuildRestoreOwnership(master.layers, out var owner, out _, out var treeDriven);

            var violations = new SortedSet<string>(StableSampleOrder.Instance);
            int violationCount = 0;
            int deadCount = 0;

            for (int layerIndex = 0; layerIndex < master.layers.Length; layerIndex++)
            {
                var layer = master.layers[layerIndex];
                if (layer?.stateMachine == null || IsProtectedLayer(layer.name))
                {
                    continue;
                }
                var onlyPlayable = LibraryDefaultState(layer);

                var owned = new List<EditorCurveBinding>();
                foreach (var pair in owner)
                {
                    if (pair.Value != layerIndex || pair.Key.type == typeof(Animator)
                        || treeDriven.Contains(pair.Key))
                    {
                        continue;
                    }
                    // A binding that no longer resolves on the avatar is a toggle over a ghost —
                    // its object went with a stripped system (SPS sockets, deleted physbone
                    // hosts). Nothing can restore it and nothing in game shows it either way, so
                    // it is not a coverage gap. The first run of this checker reported 136
                    // violations on one avatar and every visible one was this.
                    if (!AnimationUtility.GetFloatValue(ctx.Target, pair.Key, out _)
                        && !AnimationUtility.GetObjectReferenceValue(ctx.Target, pair.Key, out _))
                    {
                        deadCount++;
                        continue;
                    }
                    owned.Add(pair.Key);
                }
                if (owned.Count == 0)
                {
                    continue;
                }

                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        if (state == null || (onlyPlayable != null && state != onlyPlayable)
                            || IsPassThroughState(state))
                        {
                            continue;
                        }
                        // A tree state asserts its whole union continuously; count it as covering
                        // whatever it animates.
                        var asserts = new HashSet<EditorCurveBinding>();
                        CollectBindings(state.motion, asserts, asserts);
                        bool empty = state.motion == null || IsCurveless(state.motion, null);
                        foreach (var binding in owned)
                        {
                            if (!empty && asserts.Contains(binding))
                            {
                                continue;
                            }
                            violationCount++;
                            // The filler's own account of this layer, so a violation arrives
                            // explaining itself instead of costing a reconversion round-trip.
                            string verdict = restoreVerdicts.TryGetValue(layer.name, out string why)
                                ? why : "filler never reached this layer";
                            violations.Add($"{layer.name}/{state.name}: {binding.path}:{PrettyProperty(binding)}"
                                + (empty ? " (state asserts nothing)" : "") + $" [filler: {verdict}]");
                        }
                    }
                });
            }

            if (violationCount > 0)
            {
                ctx.Report.Warning(Category,
                    $"{violationCount} propert(ies) can still fall back to the runtime",
                    $"{string.Join("; ", violations.Take(6))}{(violations.Count > 6 ? "; …" : "")} — " +
                    "each is owned by a layer that stays silent about it in the named state. " +
                    "VRChat's runtime fills that silence with the property's default; ChilloutVR's " +
                    "does not, so if a toggle over one of these switches off and never back on in " +
                    "game, this is why. Please report the avatar — the passes that close these " +
                    "gaps believed they had.");
            }
            if (deadCount > 0)
            {
                ctx.Report.Skipped(Category,
                    $"{deadCount} animated propert(ies) point at objects that were removed",
                    "Their toggles controlled systems this conversion stripped — SPS sockets, " +
                    "deleted physics hosts — so the animation now points at nothing, and nothing " +
                    "shows in game either way. Not a coverage gap; there is nothing left to restore.");
            }
        }

        /// <summary>"blendShape.X" reads better than nothing, but strip nothing else — the report
        /// line is how a violation gets found again.</summary>
        static string PrettyProperty(EditorCurveBinding binding) =>
            string.IsNullOrEmpty(binding.propertyName) ? "(property)" : binding.propertyName;

        static void SyncDriverParameterTypes(AnimatorController master, BridgeContext ctx)
        {
            var types = new Dictionary<string, AnimatorControllerParameterType>();
            foreach (var p in master.parameters)
            {
                types[p.name] = p.type;
            }

            int corrected = 0;
            var names = new List<string>();

            AnimatorDriverTask.ParameterType Want(AnimatorControllerParameterType t)
            {
                switch (t)
                {
                    case AnimatorControllerParameterType.Int: return AnimatorDriverTask.ParameterType.Int;
                    case AnimatorControllerParameterType.Bool: return AnimatorDriverTask.ParameterType.Bool;
                    case AnimatorControllerParameterType.Trigger: return AnimatorDriverTask.ParameterType.Trigger;
                    default: return AnimatorDriverTask.ParameterType.Float;
                }
            }

            // A driver READS as well as writes. Each operand carries its own a/b/cParamType, and
            // AnimatorDriverTask.GetSourceValue switches on it to pick GetBool / GetFloat /
            // GetInteger. Those are stamped from TypeOf() when the task is built, which is BEFORE
            // ParameterTypeInference may retype the parameter — so a retype left the read side
            // pointing at the old type while the write side was corrected below.
            //
            // Unity logs "Parameter type 'Hash NNN' does not match." and returns 0 for the read,
            // once per driver execution: a tester's play-mode log carried 19,760 of them. The
            // driver then computes from a zero it should never have seen.
            void FixSource(string name, ref AnimatorDriverTask.ParameterType paramType,
                           AnimatorDriverTask.SourceType source)
            {
                if (source != AnimatorDriverTask.SourceType.Parameter || string.IsNullOrEmpty(name)
                    || !types.TryGetValue(name, out var t))
                {
                    return;   // static/random operands read nothing; unknown names aren't ours to guess
                }
                var want = Want(t);
                if (paramType != want)
                {
                    paramType = want;
                    corrected++;
                    if (names.Count < 6 && !names.Contains(name))
                    {
                        names.Add(name);
                    }
                }
            }

            void FixTasks(List<AnimatorDriverTask> tasks)
            {
                if (tasks == null)
                {
                    return;
                }
                foreach (var task in tasks)
                {
                    if (task != null)
                    {
                        FixSource(task.aName, ref task.aParamType, task.aType);
                        FixSource(task.bName, ref task.bParamType, task.bType);
                        FixSource(task.cName, ref task.cParamType, task.cType);
                    }
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
            int deadDropped = 0;
            var touched = new HashSet<string>();

            T[] Reconcile<T>(T[] transitions) where T : AnimatorTransitionBase
            {
                var survivors = new List<T>(transitions.Length);
                foreach (var transition in transitions)
                {
                    if (transition == null) continue;
                    var conditions = transition.conditions;
                    bool changed = false;
                    // Rebuilt rather than edited in place, because a tautology has to be able to
                    // leave: "> -0.001" on a bool constrains nothing, and keeping it as If states
                    // the opposite of what it said.
                    var kept = new List<AnimatorCondition>(conditions.Length);
                    bool dead = false;
                    for (int i = 0; i < conditions.Length; i++)
                    {
                        if (!types.TryGetValue(conditions[i].parameter, out var type))
                        {
                            kept.Add(conditions[i]);
                            continue;
                        }
                        var mode = conditions[i].mode;
                        float threshold = conditions[i].threshold;
                        var newMode = mode;
                        float newThreshold = threshold;
                        bool drop = false, impossible = false;

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
                                    // Greater/Less are read AGAINST THE THRESHOLD, not assumed to
                                    // mean ">0.5" and "<0.5". A bool only ever reads 0 or 1, so a
                                    // comparison outside that range is a tautology, and turning
                                    // one into If/IfNot asserts something the author never wrote.
                                    //
                                    // VRCFury writes its remote branches as the band
                                    // "IsLocal Greater -0.001 && IsLocal Less 0.001" — float for
                                    // "IsLocal is 0", i.e. this is someone else's copy. Read
                                    // blindly, the first half became If and the second IfNot, so
                                    // every NonLocal state Fury generated became unreachable on
                                    // every copy: the local branch then ran for remote viewers,
                                    // which is precisely the effect authors use these states to
                                    // avoid. Fifteen transitions on one avatar.
                                    case AnimatorConditionMode.Greater:
                                        if (threshold < 0f) { drop = true; break; }          // > -0.001: always
                                        if (threshold >= 1f) { impossible = true; break; }   // > 1: never
                                        newMode = AnimatorConditionMode.If; newThreshold = 0f; break;
                                    case AnimatorConditionMode.Less:
                                        if (threshold > 1f) { drop = true; break; }          // < 2: always
                                        if (threshold <= 0f) { impossible = true; break; }   // < 0: never
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

                        if (drop)
                        {
                            // Constrains nothing: the transition keeps its other conditions and
                            // fires on those alone, which is what the float band meant.
                            changed = true;
                            touched.Add(conditions[i].parameter);
                            fixedCount++;
                            continue;
                        }

                        var rebuilt = conditions[i];
                        if (impossible)
                        {
                            // Genuinely unsatisfiable — "> 1" or "< 0" on a value that is only
                            // ever 0 or 1. The whole transition goes, matching what
                            // ParameterTypeInference already does with an unreachable one.
                            //
                            // It is not junk in the source: VRCFury expresses "IsLocal is not 0"
                            // as an OR, and Unity ANDs conditions within a transition, so an OR
                            // needs two — one testing "< -0.001" and one "> 0.001". Only the
                            // second can ever fire for a 0/1 value; the first is the negative
                            // half of a range a bool never reaches. Dropping it leaves the live
                            // twin doing the work.
                            //
                            // Writing it as a contradictory If+IfNot pair instead (3.5.26) was
                            // correct at runtime and awful to read: it looked identical to the
                            // real bug 3.5.26 fixed, and cost a night of re-diagnosis.
                            dead = true;
                            break;
                        }

                        if (newMode != mode || !Mathf.Approximately(newThreshold, threshold))
                        {
                            rebuilt.mode = newMode;
                            rebuilt.threshold = newThreshold;
                            changed = true;
                            touched.Add(rebuilt.parameter);
                            fixedCount++;
                        }
                        kept.Add(rebuilt);
                    }
                    if (dead)
                    {
                        deadDropped++;
                        touched.Add("(unreachable transition removed)");
                        continue;
                    }
                    if (changed)
                    {
                        transition.conditions = kept.ToArray();
                    }
                    survivors.Add(transition);
                }
                return survivors.ToArray();
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    machine.anyStateTransitions = Reconcile(machine.anyStateTransitions);
                    machine.entryTransitions = Reconcile(machine.entryTransitions);
                    foreach (var child in machine.states)
                    {
                        child.state.transitions = Reconcile(child.state.transitions);
                    }
                });
            }

            if (fixedCount > 0 || deadDropped > 0)
            {
                ctx.Report.Converted(Category,
                    $"Reconciled {fixedCount} transition condition(s) to their parameter's type"
                    + (deadDropped > 0 ? $", and removed {deadDropped} transition(s) that could never fire" : ""),
                    $"A merge/inject left conditions using a comparison the parameter type can't express " +
                    $"(e.g. a bool-style If on a Float): {string.Join(", ", touched.OrderBy(n => n))}. " +
                    "ChilloutVR rejects those transitions outright, so the states never switch — this is " +
                    "what leaves face-tracking's RemoteModeActive local/remote gate dead. Comparisons are " +
                    "read against their THRESHOLD: on a parameter that only ever reads 0 or 1, \"greater " +
                    "than -0.001\" constrains nothing and is removed rather than turned into \"is true\". " +
                    "VRCFury writes its remote branches as exactly that band, and reading it the other way " +
                    "made every one of them unreachable — so the local branch played for other players, " +
                    "which is what those branches exist to prevent. A comparison that is unsatisfiable " +
                    "instead (\"< -0.001\" on the same parameter) takes its transition with it: Fury " +
                    "spells \"is not 0\" as two transitions, one per side of the range, and only the " +
                    "positive one can ever fire here. Its twin still does the work.");
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

        /// <summary>
        /// Points every blend tree parameter field at a parameter that actually exists.
        ///
        /// THIS IS A CRASH FIX, and the distinction it rests on is the whole point. Two questions
        /// look the same and are not:
        ///
        ///   - "which parameters does this avatar actually USE?" — Direct trees read neither axis
        ///     field and 1D trees ignore Y, so counting those would invent phantom references.
        ///     CollectReferencedParameters is right to skip them, and this pass does not change it.
        ///   - "which parameters must EXIST for Unity to build a graph?" — all of them. Unity binds
        ///     every blendParameter, blendParameterY and directBlendParameter it finds, vestigial
        ///     or not, and resolves each to an index in the parameter table. A name that isn't
        ///     there resolves to nothing, and the read happens inside
        ///     <c>EvaluateStateDuration → DoBlendTreeEvaluation</c> — a segfault, not an error.
        ///
        /// Answering the first question for both is what left an avatar with six undeclared blend
        /// parameters: "Blend" and "Value" and "Smooth Amount" (Unity's own defaults, left behind
        /// on VRCFury and template Direct trees), "MovementZ", a VRCFury tracking-control name, and
        /// one that was the empty string. Every one is invisible in the Animator window and fatal
        /// on the frame the graph is built.
        ///
        /// Dangling names are RENAMED with a "#" prefix and declared as Float 0 rather than
        /// declared as they stand: "#" keeps them local to the wearer so they cost no sync bits, a
        /// prefixed name cannot collide with a menu entry, and the original is still readable in
        /// the Animator window. Nothing else can reference them — anything referenced by a
        /// transition or a driver was already declared by DeclareDanglingParameters, so a blend
        /// parameter still missing at this point is referenced by this field and nothing else.
        /// </summary>
        static void SafeguardBlendParameters(AnimatorController master, BridgeContext ctx)
        {
            var declared = new HashSet<string>(master.parameters.Select(p => p.name));
            var added = new List<AnimatorControllerParameter>();
            var repointed = new SortedSet<string>(StableSampleOrder.Instance);
            var map = new Dictionary<string, string>();
            var seen = new HashSet<BlendTree>();

            string Safe(string name)
            {
                string key = name ?? "";
                if (key.Length > 0 && declared.Contains(key))
                {
                    return key;
                }
                if (map.TryGetValue(key, out string already))
                {
                    return already;
                }
                string basis = key.Length == 0 ? "AvatarBridgeUnused" : key.TrimStart('#');
                string candidate = "#" + basis;
                for (int n = 2; declared.Contains(candidate); n++)
                {
                    candidate = $"#{basis} {n}";
                }
                declared.Add(candidate);
                added.Add(new AnimatorControllerParameter
                {
                    name = candidate,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 0f
                });
                map[key] = candidate;
                repointed.Add(key.Length == 0 ? "(blank)" : key);
                return candidate;
            }

            void Walk(Motion motion)
            {
                if (!(motion is BlendTree tree) || !seen.Add(tree))
                {
                    return;
                }
                tree.blendParameter = Safe(tree.blendParameter);
                tree.blendParameterY = Safe(tree.blendParameterY);
                var kids = tree.children;
                bool changed = false;
                for (int i = 0; i < kids.Length; i++)
                {
                    string safe = Safe(kids[i].directBlendParameter);
                    if (safe != kids[i].directBlendParameter)
                    {
                        kids[i].directBlendParameter = safe;
                        changed = true;
                    }
                    Walk(kids[i].motion);
                }
                if (changed)
                {
                    // children is a value-type copy, and the setter re-derives thresholds unless
                    // automatic thresholds are off across the write.
                    bool auto = tree.useAutomaticThresholds;
                    tree.useAutomaticThresholds = false;
                    tree.children = kids;
                    tree.useAutomaticThresholds = auto;
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        if (child.state != null)
                        {
                            Walk(child.state.motion);
                        }
                    }
                });
            }

            if (added.Count == 0)
            {
                return;
            }
            // DELIBERATELY NOT DECLARED, which is the opposite of what 3.4.11's own report claimed
            // and took two versions to establish. 3.4.11 renamed the fields and — through an array
            // setter that turned out not to add anything to a controller that is already an asset —
            // declared nothing. It worked: no crash, repeated Plays, fast play mode on. 3.4.12
            // "completed" it with AddParameter, and the crash came straight back.
            //
            // So the repair is the RENAME, not the declaration. Every dangling field now names one
            // "#"-prefixed parameter instead of several different ones, and critically none of them
            // is the empty string — which is almost certainly the one that mattered, since Unity
            // resolves a missing NAME to an index of -1 and reads 0, while a blank name goes
            // somewhere else entirely. Adding the parameters for real changes what the graph builder
            // does with those trees, and on the avatar this was found on that is fatal.
            //
            // Anyone tempted to declare them: this was measured twice, in both directions, on a
            // reproducible crash. Do not do it without doing that again.
            EditorUtility.SetDirty(master);

            ctx.Report.Warning(Category,
                $"{repointed.Count} blend tree parameter(s) named something the controller never declared",
                $"{string.Join(", ", repointed)} — Unity binds EVERY blend tree parameter field when it " +
                "builds a playable graph, including the ones a Direct tree never reads and the Y axis a " +
                "1D tree ignores. One of these was BLANK, and a blank one takes the editor down with a " +
                "SIGSEGV inside DoBlendTreeEvaluation rather than an error — on Play, on selecting the " +
                "avatar, and in the CCK's uploader. \"Blend\", \"Value\" and \"Smooth Amount\" are Unity's " +
                "own defaults left behind on trees that stopped using them, so they arrive on plenty of " +
                "avatars through no fault of yours. Each field is now renamed to a single \"#\"-prefixed " +
                "name so none is blank. They are deliberately NOT declared as parameters: declaring them " +
                "was tried and brought the crash back, twice measured. Nothing changes about how the " +
                "avatar behaves — these fields were being read as garbage or not at all.");
        }

        /// <summary>
        /// Takes back menu entries this conversion invented for parameters the avatar turns out to
        /// drive itself.
        ///
        /// "Expose menuless synced parameters" exists because ChilloutVR syncs from the animator,
        /// so a synced parameter with no control still needs somewhere to live. It guesses, and the
        /// guess is wrong whenever the avatar writes that parameter from a driver: the control then
        /// sits in the menu fighting the animator for the value, which is the same objection that
        /// already keeps game-driven parameters out.
        ///
        /// It also reads as a bug. One transforming avatar came out with a "Car Mode" control (the
        /// author's, driving TransformMode) directly above a "CarMode" one (ours, driving the
        /// parameter the Action layer sets for itself) — two controls, near-identical names, one of
        /// them inert. Only entries recorded in AutoExposedParameters are eligible; anything the
        /// author put in the menu stays whatever it does.
        /// </summary>
        static void WithdrawSelfDrivenExposures(AnimatorController master, BridgeContext ctx)
        {
            var settings = ctx.CvrAvatar != null && ctx.CvrAvatar.avatarSettings != null
                ? ctx.CvrAvatar.avatarSettings.settings : null;
            if (settings == null || ctx.AutoExposedParameters.Count == 0)
            {
                return;
            }

            // Everything a parameter driver writes anywhere in the merged controller. The CCK
            // AnimatorDriver, not the VRChat one: BehaviourPass has already converted every
            // VRCAvatarParameterDriver by the time this runs, and the first version of this pass
            // scanned for the VRChat type on the merged controller — zero matches, zero withdrawn,
            // silently. The converted drivers are the same statements in the CCK's vocabulary.
            var driven = new HashSet<string>();
            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        if (child.state == null || child.state.behaviours == null)
                        {
                            continue;
                        }
                        foreach (var behaviour in child.state.behaviours)
                        {
                            if (!(behaviour is AnimatorDriver driver))
                            {
                                continue;
                            }
                            foreach (var task in (driver.EnterTasks ?? Enumerable.Empty<AnimatorDriverTask>())
                                     .Concat(driver.ExitTasks ?? Enumerable.Empty<AnimatorDriverTask>()))
                            {
                                if (task != null && !string.IsNullOrEmpty(task.targetName))
                                {
                                    driven.Add(task.targetName);
                                }
                            }
                        }
                    }
                });
            }
            if (driven.Count == 0)
            {
                return;
            }

            var withdrawn = new SortedSet<string>(StableSampleOrder.Instance);
            for (int i = settings.Count - 1; i >= 0; i--)
            {
                var entry = settings[i];
                if (entry == null || string.IsNullOrEmpty(entry.machineName)
                    || !ctx.AutoExposedParameters.Contains(entry.machineName)
                    || !driven.Contains(entry.machineName))
                {
                    continue;
                }
                withdrawn.Add($"\"{entry.name}\" ({entry.machineName})");
                settings.RemoveAt(i);
            }
            if (withdrawn.Count == 0)
            {
                return;
            }
            EditorUtility.SetDirty(ctx.CvrAvatar);
            ctx.Report.Converted(Category,
                $"{withdrawn.Count} menu control(s) withdrawn — the avatar sets these itself",
                $"{string.Join(", ", withdrawn)}. These had no control in the VRChat menu, so one was " +
                "created for them to keep them reachable; the merged animator then showed a parameter " +
                "driver writing each one. A control for a parameter the avatar drives does nothing except " +
                "fight the animator and sit next to the control that really works — the usual sighting is " +
                "two near-identically named entries where only one responds. The parameter itself is " +
                "untouched and still syncs.");
        }

        /// <summary>
        /// Replaces an animator-driven blink with ChilloutVR's native Eye Blink. Runs when
        /// DescriptorConverter found SOME blink-ish shape animated — see
        /// <c>BridgeContext.AnimatorBlinkPending</c> — but the whole decision is made HERE, where
        /// the merged layers can be inspected.
        ///
        /// It has to be, and the first version proved it the hard way: the mesh this was written
        /// for carries a shape named "Blink" AND one named "vrc.Blink". Deciding the shape first
        /// (descriptor pass) and hunting its writers second (merge pass) picked "Blink" from an
        /// expression clip, wired the native blink to it, found no strippable writer of it — and
        /// the real receiver went on driving "vrc.Blink" to 100 forever. So: find the strippable
        /// BLINK LAYER first, and let IT name the shape.
        ///
        /// A blink layer is one whose animation does nothing but blink: every float curve is a
        /// blendshape, the ONLY shape it ever raises above zero matches /blink/i, no objects, no
        /// materials. Expression layers raise other shapes and stay — an expression closing the
        /// eyes over the native blink is exactly what it did over the animator blink. The weight-0
        /// generator that flips the trigger parameter animates nothing and is left alone; its
        /// pulses land on a parameter nothing reads any more.
        ///
        /// Why the animator system can't just be kept: its "eyes open" states are EMPTY in VRChat,
        /// relying on Write Defaults to reopen the lids. Empty states crash Unity's graph builder,
        /// so conversion must fill them; a state with a motion stops writing defaults; the first
        /// blink then writes the shape to 100 and nothing ever writes it back. Eyes shut from the
        /// first blink onward — measured on the mesh itself, resting weight 0 in the prefab and
        /// 100 in the running scene.
        /// </summary>
        static void ReplaceAnimatorBlink(AnimatorController master, BridgeContext ctx)
        {
            if (!ctx.AnimatorBlinkPending || ctx.CvrAvatar == null || ctx.CvrAvatar.bodyMesh == null)
            {
                return;
            }
            var mesh = ctx.CvrAvatar.bodyMesh.sharedMesh;
            if (mesh == null)
            {
                return;
            }

            var stripped = new List<string>();
            string blinkShape = null;
            var layers = master.layers.ToList();
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                if (layer == null || IsProtectedLayer(layer.name))
                {
                    continue;
                }
                string raised = null;      // the one shape this layer raises, if it qualifies
                bool pureBlink = true, sawAnything = false;
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        var motion = child.state != null ? child.state.motion : null;
                        foreach (var clip in CollectClips(motion))
                        {
                            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0)
                            {
                                pureBlink = false;
                            }
                            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                            {
                                sawAnything = true;
                                if (!binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                                {
                                    pureBlink = false;
                                    continue;
                                }
                                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                                if (curve == null || !curve.keys.Any(k => Mathf.Abs(k.value) > 0.001f))
                                {
                                    continue; // held at zero: the receiver resetting a co-shape
                                }
                                string shape = binding.propertyName.Substring("blendShape.".Length);
                                if (shape.IndexOf("blink", StringComparison.OrdinalIgnoreCase) < 0
                                    || (raised != null && raised != shape))
                                {
                                    pureBlink = false; // raises a non-blink shape, or two shapes
                                }
                                else
                                {
                                    raised = shape;
                                }
                            }
                        }
                    }
                });
                if (pureBlink && sawAnything && raised != null && mesh.GetBlendShapeIndex(raised) >= 0)
                {
                    blinkShape = raised;
                    stripped.Add(layer.name);
                    layers.RemoveAt(i);
                }
            }

            if (blinkShape == null)
            {
                // Nothing safely strippable. Leave the avatar's own system in place and say so —
                // adding the native blink on top would put two systems on one pair of eyes.
                //
                // But FILL THE SHAPE SLOTS anyway. They are inert while the tickbox is off, and
                // leaving them empty made the recovery worse than the problem: a tester whose eyes
                // never blinked had to work out for themselves which of nine blink-ish shapes on a
                // 239-shape mesh the client wanted, when detection had already picked them. Now the
                // fix is the one tick the report names, and the shapes are already in place.
                AvatarFeatureDetect.DetectBlinkShapes(mesh, out string fbLeft, out string fbRight,
                    out string fbCombined);
                string prefilled = null;
                if (fbLeft != null || fbRight != null || fbCombined != null)
                {
                    if (ctx.CvrAvatar.blinkBlendshape == null || ctx.CvrAvatar.blinkBlendshape.Length < 4)
                    {
                        ctx.CvrAvatar.blinkBlendshape = new string[4];
                    }
                    if (fbLeft != null && fbRight != null)
                    {
                        ctx.CvrAvatar.blinkBlendshape[0] = fbLeft;
                        ctx.CvrAvatar.blinkBlendshape[1] = fbRight;
                        prefilled = $"\"{fbLeft}\" / \"{fbRight}\"";
                    }
                    else
                    {
                        var single = fbCombined ?? fbLeft ?? fbRight;
                        ctx.CvrAvatar.blinkBlendshape[0] = single;
                        prefilled = $"\"{single}\"";
                    }
                    EditorUtility.SetDirty(ctx.CvrAvatar);
                }

                ctx.Report.Approximated(Category, "Blink left to the avatar's own animation",
                    "This avatar blinks from its own animator, but no layer could be safely identified " +
                    "as ONLY blinking, so nothing was removed and ChilloutVR's native blink stays off. " +
                    "If the eyes stick closed in game, this is where to look: the animator blink relies " +
                    "on empty-state Write Defaults behaviour that does not survive conversion. " +
                    (prefilled != null
                        ? $"The blink shapes are already filled in on the CVRAvatar ({prefilled}), so the " +
                          "fix is one tick: turn ON \"Use Blink Blendshapes\". Only do that if the eyes " +
                          "DON'T blink in game — with both systems running, the client's blink overwrites " +
                          "that shape every frame in LateUpdate and any expression using it stops closing " +
                          "the eyes."
                        : "No blink-ish blendshape could be found on the body mesh either, so the shape " +
                          "slots are empty; naming one on the CVRAvatar and ticking \"Use Blink " +
                          "Blendshapes\" is the manual fix."));
                return;
            }

            master.layers = layers.ToArray();
            EditorUtility.SetDirty(master);

            // ChilloutVR's blink is NOT an animation layer, and that changes who wins.
            // EyeMovementController.ProcessBlinking runs in LateUpdate and writes the weight
            // straight onto the mesh, after the animator has finished — so whichever shape is
            // handed to it, the client owns that shape outright, every frame. Pointing it at a
            // shape an expression still animates does not lose an occasional frame; it flattens
            // that expression for good, and the eyes simply stop closing on that gesture.
            //
            // Meshes that blink usually offer more than one way to do it — a separate L/R pair
            // beside the combined shape, which is what a "for best blink use two" label on a mesh
            // is telling authors. So when the removed layer's shape is contested, move the native
            // blink onto a family nothing else drives: the eyes still blink, and the expression
            // still closes them its own way.
            var contested = RaisedFaceShapes(master, ctx.CvrAvatar);
            string pairLeft = null, pairRight = null;
            string chosen = blinkShape;
            string movedTo = null;
            if (contested.Contains(blinkShape))
            {
                AvatarFeatureDetect.DetectBlinkShapes(mesh, out string spareLeft, out string spareRight,
                    out string spareCombined);
                if (spareLeft != null && spareRight != null
                    && !contested.Contains(spareLeft) && !contested.Contains(spareRight))
                {
                    pairLeft = spareLeft;
                    pairRight = spareRight;
                    movedTo = $"\"{spareLeft}\" / \"{spareRight}\"";
                }
                else if (spareCombined != null && spareCombined != blinkShape
                    && !contested.Contains(spareCombined))
                {
                    chosen = spareCombined;
                    movedTo = $"\"{spareCombined}\"";
                }
            }

            var cvrAvatar = ctx.CvrAvatar;
            cvrAvatar.useBlinkBlendshapes = true;
            if (cvrAvatar.blinkBlendshape == null || cvrAvatar.blinkBlendshape.Length < 4)
            {
                cvrAvatar.blinkBlendshape = new string[4];
            }
            // Cleared first: Combined mode drives ALL FOUR slots at once, so a leftover name in a
            // slot this conversion did not fill would be driven along with the blink.
            for (int slot = 0; slot < cvrAvatar.blinkBlendshape.Length; slot++)
            {
                cvrAvatar.blinkBlendshape[slot] = null;
            }
            if (pairLeft != null)
            {
                cvrAvatar.blinkBlendshape[0] = pairLeft;
                cvrAvatar.blinkBlendshape[1] = pairRight;
                AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Separate");
            }
            else
            {
                cvrAvatar.blinkBlendshape[0] = chosen;
                AvatarFeatureDetect.SetBlinkMode(cvrAvatar, "Combined");
            }
            // Live weights may be whatever the old system last wrote — the source scene arrives
            // mid-blink if a previous session stuck it closed. Native blink owns these now, and
            // its rest is open. The removed layer's own shape is zeroed too even when the blink
            // moved off it, because nothing writes it until an expression does.
            foreach (string shape in new[] { blinkShape, chosen, pairLeft, pairRight })
            {
                int index = string.IsNullOrEmpty(shape) ? -1 : mesh.GetBlendShapeIndex(shape);
                if (index >= 0)
                {
                    cvrAvatar.bodyMesh.SetBlendShapeWeight(index, 0f);
                }
            }
            EditorUtility.SetDirty(cvrAvatar);

            const string why =
                "This system existed because VRChat has no built-in blink, and it cannot survive " +
                "conversion: its \"eyes open\" states are EMPTY in VRChat and rely on Write Defaults " +
                "reopening the lids. Empty states crash Unity's graph builder, the filler that " +
                "prevents that is a motion, and a state with a motion stops writing defaults — so the " +
                "first blink closed the eyes for good.";

            if (movedTo != null)
            {
                ctx.Report.Converted(Category,
                    $"Blink converted to ChilloutVR's native blink — {movedTo}, {stripped.Count} layer(s) removed",
                    $"{string.Join(", ", stripped)} blinked \"{blinkShape}\". {why} \"{blinkShape}\" is also " +
                    "used by this avatar's expressions, and ChilloutVR writes its blink shape onto the mesh " +
                    "every frame AFTER the animator — so aiming the native blink there would have flattened " +
                    $"them, and the eyes would have stopped closing on those gestures. Wired to {movedTo} " +
                    "instead, which nothing else animates, so both work.");
            }
            else if (contested.Contains(blinkShape))
            {
                ctx.Report.Warning(Category,
                    $"Blink and expressions share \"{blinkShape}\"",
                    $"{string.Join(", ", stripped)} blinked \"{blinkShape}\", and ChilloutVR's native blink " +
                    $"now drives it. {why} The catch: expressions on this avatar animate \"{blinkShape}\" " +
                    "too, and ChilloutVR writes the blink shape onto the mesh every frame AFTER the " +
                    "animator, so the blink wins and those expressions will not close the eyes. No other " +
                    "blink shape on the mesh was free to move to. If you have a spare eyelid shape, point " +
                    "Eye Blink Settings at it on the CVRAvatar.");
            }
            else
            {
                ctx.Report.Converted(Category,
                    $"Blink converted to ChilloutVR's native blink — \"{blinkShape}\", {stripped.Count} layer(s) removed",
                    $"{string.Join(", ", stripped)} blinked \"{blinkShape}\". {why} ChilloutVR's native Eye " +
                    "Blink now drives the exact shape the removed layer drove, and nothing else animates it. " +
                    "Expressions that close the eyes through a different shape are untouched.");
            }
        }

        /// <summary>
        /// Every blendshape name some surviving clip drives above zero on the avatar's face mesh.
        ///
        /// Held-at-zero curves deliberately do not count: a clip that only ever writes 0 to a
        /// shape is asking for the same thing ChilloutVR's blink asks for between blinks, so
        /// nothing is lost by sharing it. The path filter matters because avatars carry the same
        /// shape names on several meshes — a "Blink" on a spare head is not competition for the
        /// one the client will drive.
        /// </summary>
        static HashSet<string> RaisedFaceShapes(AnimatorController master, CVRAvatar cvrAvatar)
        {
            var raised = new HashSet<string>(StringComparer.Ordinal);
            var face = cvrAvatar != null ? cvrAvatar.bodyMesh : null;
            if (face == null)
            {
                return raised;
            }
            string facePath = face.transform.IsChildOf(cvrAvatar.transform)
                ? AnimationUtility.CalculateTransformPath(face.transform, cvrAvatar.transform)
                : null;

            foreach (var clip in master.animationClips)
            {
                if (clip == null)
                {
                    continue;
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    const string prefix = "blendShape.";
                    if (!binding.propertyName.StartsWith(prefix, StringComparison.Ordinal)
                        || (facePath != null && binding.path != facePath))
                    {
                        continue;
                    }
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || !curve.keys.Any(k => Mathf.Abs(k.value) > 0.001f))
                    {
                        continue;
                    }
                    raised.Add(binding.propertyName.Substring(prefix.Length));
                }
            }
            return raised;
        }

        /// <summary>
        /// Rebuilds a VRChat Action feature's pose states inside ChilloutVR's own
        /// Locomotion/Emotes layer, which is the one place on this platform a full-body pose can
        /// both assert and let go.
        ///
        /// The problem it solves: VRChat's Action playable rests at weight 0 and is raised at
        /// runtime by behaviours while a sequence plays. ChilloutVR has no runtime layer-weight
        /// control, and a SEPARATE layer has no way to yield — inert states with Write Defaults
        /// off hold the last written muscles, Write Defaults on asserts rest pose over locomotion.
        /// Five versions of state surgery hit that wall. Inside the locomotion layer the wall does
        /// not exist: when the pose states aren't active, the layer's own locomotion states are,
        /// still writing muscles every frame. Handing back IS yielding.
        ///
        /// What moves is the LIVE WINDOW — the states reachable from a behaviour that raises the
        /// Action weight (goalWeight 1) without passing one that fades it (goalWeight 0), read
        /// from the SOURCE layer's behaviours before they're stripped. Those are exactly the
        /// states VRChat ever showed. Entry: each source transition from outside the window into a
        /// raise-state becomes a transition FROM the locomotion resting state carrying the same conditions — the avatar's
        /// own arming logic, gestures and all. Exit: each transition from a window state to a
        /// fade-state becomes a transition to the locomotion layer's default state. Behaviours are
        /// NOT copied: the original layer stays merged at weight 0, where its parameter drivers
        /// keep firing exactly as VRChat's did.
        /// </summary>
        /// <summary>States whose SOURCE behaviours fade the Action playable to 0 — where VRChat
        /// stopped showing the layer.</summary>
        static HashSet<string> LayerOffStates(AnimatorControllerLayer source)
            => LayerWeightStates(source, on: false);

        /// <summary>States whose behaviours raise the Action playable to 1 — where VRChat started
        /// showing it. The entry points of the live window.</summary>
        static HashSet<string> LayerOnStates(AnimatorControllerLayer source)
            => LayerWeightStates(source, on: true);

        static HashSet<string> LayerWeightStates(AnimatorControllerLayer source, bool on)
        {
            var found = new HashSet<string>();
            if (source == null || source.stateMachine == null)
            {
                return found;
            }
            WalkMachines(source.stateMachine, machine =>
            {
                foreach (var child in machine.states)
                {
                    var state = child.state;
                    if (state == null || state.behaviours == null)
                    {
                        continue;
                    }
                    foreach (var behaviour in state.behaviours)
                    {
                        // BOTH behaviour types: VRCPlayableLayerControl drives the whole playable's
                        // weight (the standard way an Action system runs itself), and
                        // VRCAnimatorLayerControl drives one layer inside a playable. Only the
                        // Action playable counts either way — a state may also drive FX or Gesture
                        // weights, and those say nothing about this layer's visibility.
                        bool matches =
                            (behaviour is VRC.SDK3.Avatars.Components.VRCPlayableLayerControl playable
                                && playable.layer == VRC.SDKBase.VRC_PlayableLayerControl.BlendableLayer.Action
                                && (on ? playable.goalWeight >= 0.999f : playable.goalWeight <= 0.001f))
                            || (behaviour is VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl animator
                                && animator.playable == VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.Action
                                && (on ? animator.goalWeight >= 0.999f : animator.goalWeight <= 0.001f));
                        if (matches)
                        {
                            found.Add(state.name);
                        }
                    }
                }
            });
            return found;
        }

        static int TransplantActionFeature(AnimatorController master, List<AnimatorControllerLayer> masterLayers,
            AnimatorControllerLayer clone, AnimatorControllerLayer source, BridgeContext ctx)
        {
            try
            {
                var locomotion = masterLayers.FirstOrDefault(l =>
                    l != null && l.name == "Locomotion/Emotes" && l.stateMachine != null);
                var locoDefault = locomotion != null ? locomotion.stateMachine.defaultState : null;
                if (locoDefault == null || clone.stateMachine == null)
                {
                    return 0;
                }

                var on = LayerWeightStates(source, on: true);
                var off = LayerWeightStates(source, on: false);
                if (on.Count == 0)
                {
                    return 0;
                }

                // The clone's states, flat. Names are unique within a VRChat layer in practice;
                // a duplicate would only shadow a state of the same name, not corrupt anything.
                var byName = new Dictionary<string, AnimatorState>();
                WalkMachines(clone.stateMachine, m =>
                {
                    foreach (var child in m.states)
                    {
                        if (child.state != null && !byName.ContainsKey(child.state.name))
                        {
                            byName[child.state.name] = child.state;
                        }
                    }
                });

                var live = new HashSet<string>(on.Where(byName.ContainsKey));
                if (live.Count == 0)
                {
                    return 0;
                }
                var queue = new Queue<string>(live);
                while (queue.Count > 0)
                {
                    var state = byName[queue.Dequeue()];
                    foreach (var transition in state.transitions)
                    {
                        var next = transition != null ? transition.destinationState : null;
                        if (next != null && !off.Contains(next.name) && byName.ContainsKey(next.name)
                            && live.Add(next.name))
                        {
                            queue.Enqueue(next.name);
                        }
                    }
                }

                // Rebuild the window inside the locomotion machine.
                var locoMachine = locomotion.stateMachine;
                var copies = new Dictionary<string, AnimatorState>();
                foreach (var name in live)
                {
                    var src = byName[name];
                    var copy = locoMachine.AddState($"[AB] {name}");
                    // Pose states get the clip without baked TRAVEL: a VRChat feature that moved
                    // the player did it by animating the body (VRChat allows nothing else), and
                    // here that displaces the wearer's camera with no input. Root motion that
                    // returns home — a backflip's flip, a dance's sway — is kept: stripping it
                    // broke the animations while removing nothing a player could feel.
                    // keepVertical: a transformation lowering the body to the floor is the avatar
                    // changing its own HEIGHT, not travelling — flattening that left it standing
                    // through the whole transition and snapping down at the end. Rotation is NOT
                    // kept: the client's capsule is always upright, so a RootQ curve does nothing
                    // in game and keeping it only made editor and game disagree.
                    copy.motion = src.motion is AnimationClip poseClip
                        ? LocomotionGrafter.WithoutRootMotion(poseClip, onlyIfTravels: true,
                            keepPose: true)
                        : src.motion;
                    copy.speed = src.speed;
                    copy.writeDefaultValues = src.writeDefaultValues;
                    copies[name] = copy;
                }

                void CopyTransition(AnimatorStateTransition from, AnimatorStateTransition to)
                {
                    to.hasExitTime = from.hasExitTime;
                    to.exitTime = from.exitTime;
                    to.hasFixedDuration = from.hasFixedDuration;
                    to.duration = from.duration;
                    to.offset = from.offset;
                    foreach (var condition in from.conditions)
                    {
                        to.AddCondition(condition.mode, condition.threshold, condition.parameter);
                    }
                }

                foreach (var name in live)
                {
                    var src = byName[name];
                    foreach (var transition in src.transitions)
                    {
                        var dst = transition != null ? transition.destinationState : null;
                        if (dst != null && copies.TryGetValue(dst.name, out var innerDst))
                        {
                            CopyTransition(transition, copies[name].AddTransition(innerDst));
                        }
                        else if (transition != null)
                        {
                            // Leaving the window (the fade state, or anywhere else): hand the
                            // body back to locomotion, blending rather than snapping.
                            var exit = copies[name].AddTransition(locoDefault);
                            CopyTransition(transition, exit);
                            exit.hasFixedDuration = true;
                            exit.duration = 0.25f;
                        }
                    }
                }

                // Arming: every way the source machine could ENTER the window from outside it
                // becomes an AnyState transition with the same conditions. Unconditional entries
                // are skipped — an AnyState transition with no conditions would fire every frame.
                int armed = 0;
                var armedSignatures = new HashSet<string>();
                var armedEntries = new List<(AnimatorStateTransition entry, AnimatorCondition[] conditions)>();
                void Arm(AnimatorStateTransition transition)
                {
                    var dst = transition != null ? transition.destinationState : null;
                    if (dst == null || !on.Contains(dst.name) || !copies.TryGetValue(dst.name, out var target)
                        || transition.conditions.Length == 0)
                    {
                        return;
                    }
                    // From the locomotion layer's RESTING state, never AnyState. AnyState re-fires
                    // from INSIDE the window — canTransitionToSelf only blocks the entry state
                    // re-entering itself, so with the arming parameter still set the machine looped
                    // Transformation → Car_Idle → AnyState → Transformation, visibly flickering.
                    // In the source graph re-entry was impossible POSITIONALLY: the arming
                    // transition left a state the window never returns to. Arming from the resting
                    // state reproduces that: once inside, nothing can re-fire until the pose has
                    // handed back AND the conditions have gone false and true again.
                    string signature = dst.name + "|" + string.Join(",",
                        transition.conditions.Select(c => $"{c.parameter}{(int)c.mode}{c.threshold}"));
                    if (!armedSignatures.Add(signature))
                    {
                        return;
                    }
                    var entry = locoDefault.AddTransition(target);
                    entry.hasExitTime = false;
                    entry.hasFixedDuration = true;
                    entry.duration = 0.1f;
                    foreach (var condition in transition.conditions)
                    {
                        entry.AddCondition(condition.mode, condition.threshold, condition.parameter);
                    }
                    armedEntries.Add((entry, transition.conditions));
                    armed++;
                }
                WalkMachines(clone.stateMachine, m =>
                {
                    foreach (var child in m.states)
                    {
                        if (child.state == null || live.Contains(child.state.name))
                        {
                            continue;
                        }
                        foreach (var transition in child.state.transitions)
                        {
                            Arm(transition);
                        }
                    }
                    foreach (var transition in m.anyStateTransitions)
                    {
                        Arm(transition);
                    }
                });
                if (armed == 0)
                {
                    // No way in: remove what was added rather than leave dead states around.
                    foreach (var copy in copies.Values)
                    {
                        locoMachine.RemoveState(copy);
                    }
                    return 0;
                }

                // ---- edge-triggered arming ------------------------------------------------
                // Arming on a LEVEL replays forever: a dropdown-style menu HOLDS its value, so
                // the moment a played-once pose hands back to the resting state, its conditions
                // are still true and it re-enters — Wave forever. VRChat never replays because
                // its graph PARKS after an emote in a state whose only way back requires the
                // value to return to zero: entry fires on the RISE of the conditions, once.
                //
                // Reproduced in parameter form. A "#" ready flag (local — every client computes
                // its own from the synced inputs) gates every arming transition, and a weight-0
                // memory layer manages it: its Ready state raises the flag; the instant any
                // arming signature's conditions come true it moves to an Engaged state that
                // drops the flag; it returns to Ready — re-raising the flag — only when those
                // conditions have gone FALSE again. The window itself evaluates before the
                // memory layer (lower index), so the one frame of flag-up is exactly enough to
                // arm once per rise.
                string readyName = "#AB_Ready_" + SanitizeParameterName(clone.name);
                if (master.parameters.All(p => p.name != readyName))
                {
                    var withReady = master.parameters.ToList();
                    withReady.Add(new AnimatorControllerParameter
                    {
                        name = readyName,
                        // DISARMED at load. See the "Rest" prologue below: this used to default
                        // to 1, which assumed the arming conditions are false at rest.
                        type = AnimatorControllerParameterType.Float,
                        defaultFloat = 0f
                    });
                    master.parameters = withReady.ToArray();
                }
                foreach (var (entry, _) in armedEntries)
                {
                    entry.AddCondition(AnimatorConditionMode.Greater, 0.5f, readyName);
                }

                AnimatorDriverTask Task(string targetName, AnimatorDriverTask.Operator op,
                    string aParam, float aStatic, string bParam)
                {
                    var task = new AnimatorDriverTask
                    {
                        targetType = AnimatorDriverTask.ParameterType.Float,
                        targetName = targetName,
                        op = op
                    };
                    if (aParam != null)
                    {
                        task.aType = AnimatorDriverTask.SourceType.Parameter;
                        task.aParamType = AnimatorDriverTask.ParameterType.Float;
                        task.aName = aParam;
                    }
                    else
                    {
                        task.aType = AnimatorDriverTask.SourceType.Static;
                        task.aValue = aStatic;
                    }
                    if (bParam != null)
                    {
                        task.bType = AnimatorDriverTask.SourceType.Parameter;
                        task.bParamType = AnimatorDriverTask.ParameterType.Float;
                        task.bName = bParam;
                    }
                    return task;
                }
                string deltaName = readyName + "_delta";
                string compareName = readyName + "_cmp";
                var scratches = new List<string> { deltaName, compareName };
                string PrevName(string parameter) => readyName + "_prev_" + SanitizeParameterName(parameter);

                // The tick that gives Engaged states a length, so their exit-time self-loop
                // re-runs the change check every cycle. Same idiom as the velocity feed.
                var armingTick = new AnimationClip { name = "Arming Tick" };
                armingTick.SetCurve("", typeof(Animator), compareName + "Tick",
                    AnimationCurve.Constant(0f, 1f / 60f, 0f));

                var memory = new AnimatorStateMachine
                {
                    name = $"{clone.name} Arming",
                    hideFlags = HideFlags.HideInHierarchy
                };
                var ready = memory.AddState("Ready");
                ready.writeDefaultValues = false;
                ready.motion = armingTick;
                memory.defaultState = ready;
                var readyDriver = ready.AddStateMachineBehaviour<AnimatorDriver>();
                readyDriver.localOnly = false;
                readyDriver.EnterTasks.Add(Task(readyName, AnimatorDriverTask.Operator.Set, null, 1f, null));
                int engagedCount = 0;
                foreach (var (_, conditions) in armedEntries)
                {
                    engagedCount++;
                    var parameters = conditions.Select(c => c.parameter).Distinct().ToList();

                    // Capture: drop the flag and remember the values that armed. A separate
                    // state, because Engaged re-enters itself every tick to re-run its check —
                    // capturing there would re-baseline every cycle and never see a change.
                    var capture = memory.AddState($"Capture {engagedCount}");
                    capture.writeDefaultValues = false;
                    capture.motion = armingTick;
                    var captureDriver = capture.AddStateMachineBehaviour<AnimatorDriver>();
                    captureDriver.localOnly = false;
                    captureDriver.EnterTasks.Add(Task(readyName, AnimatorDriverTask.Operator.Set, null, 0f, null));
                    foreach (var parameter in parameters)
                    {
                        scratches.Add(PrevName(parameter));
                        captureDriver.EnterTasks.Add(Task(PrevName(parameter),
                            AnimatorDriverTask.Operator.Set, parameter, 0f, null));
                    }

                    // Engaged: every cycle, delta = OR over parameters of (value != captured).
                    var engaged = memory.AddState($"Engaged {engagedCount}");
                    engaged.writeDefaultValues = false;
                    engaged.motion = armingTick;
                    var engagedDriver = engaged.AddStateMachineBehaviour<AnimatorDriver>();
                    engagedDriver.localOnly = false;
                    engagedDriver.EnterTasks.Add(Task(deltaName, AnimatorDriverTask.Operator.Set, null, 0f, null));
                    foreach (var parameter in parameters)
                    {
                        engagedDriver.EnterTasks.Add(Task(compareName,
                            AnimatorDriverTask.Operator.NotEqual, parameter, 0f, PrevName(parameter)));
                        engagedDriver.EnterTasks.Add(Task(deltaName,
                            AnimatorDriverTask.Operator.Addition, deltaName, 0f, compareName));
                    }

                    var hold = ready.AddTransition(capture);
                    hold.hasExitTime = false;
                    hold.hasFixedDuration = true;
                    hold.duration = 0f;
                    foreach (var condition in conditions)
                    {
                        hold.AddCondition(condition.mode, condition.threshold, condition.parameter);
                    }
                    var settle = capture.AddTransition(engaged);
                    settle.hasExitTime = true;
                    settle.exitTime = 1f;
                    settle.hasFixedDuration = true;
                    settle.duration = 0f;
                    var loop = engaged.AddTransition(engaged);
                    loop.hasExitTime = true;
                    loop.exitTime = 1f;
                    loop.hasFixedDuration = true;
                    loop.duration = 0f;

                    // Release and re-raise the flag when the arming conditions have gone FALSE —
                    // the complement of (A AND B) is (!A OR !B), one transition per negation —
                    // OR when any armed parameter has CHANGED while they stayed true. The change
                    // release is what lets a held menu switch straight from one emote to the
                    // next: Wave -> Dab used to need a trip through None, because "0 < VRCEmote
                    // < 9" never went false between them.
                    foreach (var condition in conditions)
                    {
                        var release = engaged.AddTransition(ready);
                        release.hasExitTime = false;
                        release.hasFixedDuration = true;
                        release.duration = 0f;
                        var (mode, threshold) = InvertCondition(condition);
                        release.AddCondition(mode, threshold, condition.parameter);
                    }
                    var changed = engaged.AddTransition(ready);
                    changed.hasExitTime = false;
                    changed.hasFixedDuration = true;
                    changed.duration = 0f;
                    changed.AddCondition(AnimatorConditionMode.Greater, 0.5f, deltaName);
                }
                // ---- disarmed until something actually changes ----------------------------
                // The ready flag used to default to 1, which assumes the arming conditions are
                // FALSE when the avatar loads. Plenty are not. A VRChat Action layer sits at
                // WEIGHT 0 until its feature raises it, so conditions inside it are free to be
                // permanently true — nothing plays, because the whole layer is silent. This
                // transplant reproduces the transitions but not the weight gate, so the same
                // condition fires the instant the avatar loads in CVR's always-on locomotion
                // layer.
                //
                // Two ways that presented, both found by the regression corpus: an inflation rig
                // whose window exit was ALSO true at rest ping-ponged between the pose and
                // LocIdle forever, and a second one whose states chain on exit time walked up
                // its stages at load and parked there — a bicycle pose nobody asked for.
                //
                // So the machine starts DISARMED, snapshots the values it woke up with, and arms
                // only once one of them departs from that snapshot: a real user action, rather
                // than the mere fact of the avatar existing. Everything after the first arm is
                // unchanged — Engaged still releases on conditions-false or value-change.
                var restParameters = armedEntries
                    .SelectMany(e => e.Item2.Select(c => c.parameter))
                    .Distinct()
                    .ToList();

                var rest = memory.AddState("Rest");
                rest.writeDefaultValues = false;
                rest.motion = armingTick;
                var restDriver = rest.AddStateMachineBehaviour<AnimatorDriver>();
                restDriver.localOnly = false;
                restDriver.EnterTasks.Add(Task(readyName, AnimatorDriverTask.Operator.Set, null, 0f, null));
                foreach (var parameter in restParameters)
                {
                    scratches.Add(PrevName(parameter));
                    restDriver.EnterTasks.Add(Task(PrevName(parameter),
                        AnimatorDriverTask.Operator.Set, parameter, 0f, null));
                }

                // Same split as Capture/Engaged, and for the same reason: a state's enter tasks
                // run once, so the snapshot and the comparison cannot live in one state or the
                // baseline would be rewritten every tick and never register a change.
                var watch = memory.AddState("Rest Watch");
                watch.writeDefaultValues = false;
                watch.motion = armingTick;
                var watchDriver = watch.AddStateMachineBehaviour<AnimatorDriver>();
                watchDriver.localOnly = false;
                watchDriver.EnterTasks.Add(Task(deltaName, AnimatorDriverTask.Operator.Set, null, 0f, null));
                foreach (var parameter in restParameters)
                {
                    watchDriver.EnterTasks.Add(Task(compareName,
                        AnimatorDriverTask.Operator.NotEqual, parameter, 0f, PrevName(parameter)));
                    watchDriver.EnterTasks.Add(Task(deltaName,
                        AnimatorDriverTask.Operator.Addition, deltaName, 0f, compareName));
                }

                memory.defaultState = rest;

                var settleRest = rest.AddTransition(watch);
                settleRest.hasExitTime = true;
                settleRest.exitTime = 1f;
                settleRest.hasFixedDuration = true;
                settleRest.duration = 0f;

                var watchLoop = watch.AddTransition(watch);
                watchLoop.hasExitTime = true;
                watchLoop.exitTime = 1f;
                watchLoop.hasFixedDuration = true;
                watchLoop.duration = 0f;

                var wake = watch.AddTransition(ready);
                wake.hasExitTime = false;
                wake.hasFixedDuration = true;
                wake.duration = 0f;
                wake.AddCondition(AnimatorConditionMode.Greater, 0.5f, deltaName);

                var withScratches = master.parameters.ToList();
                foreach (var scratch in scratches.Distinct())
                {
                    if (withScratches.All(p => p.name != scratch))
                    {
                        withScratches.Add(new AnimatorControllerParameter
                        {
                            name = scratch,
                            type = AnimatorControllerParameterType.Float,
                            defaultFloat = 0f
                        });
                    }
                }
                master.parameters = withScratches.ToArray();
                masterLayers.Add(new AnimatorControllerLayer
                {
                    name = MakeUniqueLayerName(masterLayers, $"[AB] {clone.name} Arming"),
                    defaultWeight = 0f, // drivers run regardless of weight; nothing to show
                    stateMachine = memory
                });

                return copies.Count;
            }
            catch (Exception e)
            {
                ctx.Report.Warning(Category, $"Could not move \"{clone.name}\"'s poses into the locomotion layer",
                    $"{e.GetType().Name}: {e.Message} — the layer stays at weight 0; its visible FX still work.");
                return 0;
            }
        }

        /// <summary>
        /// The condition that is true exactly when the given one is false. Greater/Less get a
        /// hair's-width shift so integers on the boundary land on the right side: the complement
        /// of "&gt; 0" must accept 0 itself.
        /// </summary>
        static (AnimatorConditionMode mode, float threshold) InvertCondition(AnimatorCondition condition)
        {
            switch (condition.mode)
            {
                case AnimatorConditionMode.If: return (AnimatorConditionMode.IfNot, 0f);
                case AnimatorConditionMode.IfNot: return (AnimatorConditionMode.If, 0f);
                case AnimatorConditionMode.Greater:
                    return (AnimatorConditionMode.Less, condition.threshold + 0.0001f);
                case AnimatorConditionMode.Less:
                    return (AnimatorConditionMode.Greater, condition.threshold - 0.0001f);
                case AnimatorConditionMode.Equals:
                    return (AnimatorConditionMode.NotEqual, condition.threshold);
                default:
                    return (AnimatorConditionMode.Equals, condition.threshold);
            }
        }

        /// <summary>
        /// VRChat's built-ins that an Action layer legitimately waits on while doing nothing but
        /// playing emotes. Anything OUTSIDE this set means the avatar is driving the layer itself.
        /// </summary>
        static readonly HashSet<string> EmotePlayerParameters = new HashSet<string>
        {
            "VRCEmote", "VRCFaceBlendH", "VRCFaceBlendV", "AFK", "Seated", "InStation",
            "IsLocal", "Upright", "Grounded", "Supine", "Voice", "Sitting", "TrackingType",
        };

        /// <summary>
        /// Whether an Action layer is a FEATURE the avatar drives, rather than VRChat's emote
        /// player. The distinction decides whether the layer may be merged live.
        ///
        /// VRChat keeps the Action playable layer at weight 0 and raises it only while an emote
        /// runs, so its idle state can hold a full-body clip and harm nothing. ChilloutVR has no
        /// playable layers, so an Action layer merged at weight 1 asserts that idle over locomotion
        /// — hence the blanket weight 0, which is right for emotes and fatal for anything else.
        ///
        /// "Anything else" is real and not rare: a transforming robot avatar put its entire
        /// car-mode sequence in an Action layer gated on its own CarMode/TransformMode parameters.
        /// Every parameter converted, the menu toggled them correctly, and nothing happened,
        /// because the layer holding the animation could not reach any weight.
        ///
        /// The test is what the layer WAITS ON. A transition naming a parameter that isn't one of
        /// VRChat's emote/state built-ins is the avatar asking for this layer on purpose.
        /// </summary>
        static bool ActionLayerDrivesOwnFeature(AnimatorControllerLayer layer, out string byWhat)
        {
            byWhat = null;
            var own = new SortedSet<string>(StableSampleOrder.Instance);
            WalkMachines(layer.stateMachine, machine =>
            {
                void Note(AnimatorTransitionBase transition)
                {
                    if (transition == null || transition.conditions == null)
                    {
                        return;
                    }
                    foreach (var condition in transition.conditions)
                    {
                        if (!string.IsNullOrEmpty(condition.parameter)
                            && !EmotePlayerParameters.Contains(condition.parameter)
                            && !condition.parameter.StartsWith("#", StringComparison.Ordinal))
                        {
                            own.Add(condition.parameter);
                        }
                    }
                }
                foreach (var child in machine.states)
                {
                    if (child.state != null)
                    {
                        foreach (var transition in child.state.transitions)
                        {
                            Note(transition);
                        }
                    }
                }
                foreach (var transition in machine.anyStateTransitions)
                {
                    Note(transition);
                }
                foreach (var transition in machine.entryTransitions)
                {
                    Note(transition);
                }
            });
            if (own.Count == 0)
            {
                return false;
            }
            byWhat = string.Join(", ", own.Take(4)) + (own.Count > 4 ? ", …" : "");
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
                (bare: "TrackingType", streamType: "LocalPlayerFullBodyEnabled", app: "Remap", lo: 3f, hi: 6f),

                // ChilloutVR's AvatarHeight is the calibrated avatar height in metres — the same
                // measure AvatarUpright divides by. It is the platform's closest reading of
                // VRChat's EyeHeightAsMeters, and everything else in VRChat's scale family
                // (ScaleFactor and friends) is derived from this one value by FeedScaleParameters.
                (bare: "EyeHeightAsMeters", streamType: "AvatarHeight", app: "Override", lo: 0f, hi: 0f)
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
                { "TriggerRightValue", 280 }, { "AvatarHeight", 400 }, { "AvatarUpright", 401 },
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
        /// <summary>
        /// Disables "Can Transition To Self" on merged AnyState transitions whose conditions
        /// carry no Trigger — because with only level conditions, that flag means "re-enter the
        /// destination EVERY FRAME the conditions hold", restarting its motion each time.
        ///
        /// Unity defaults the flag to on and authors rarely touch it, so nearly every avatar
        /// carries dozens of these. VRChat never shows the problem: the states involved are
        /// mostly EMPTY there (behaviour-only gates), and restarting nothing looks like nothing.
        /// Conversion must fill empty states (they crash Unity's graph builder), and a filled
        /// state restarted every frame strobes its clip — reported as animations rapidly
        /// flickering, characteristically on OTHER players' screens: remote copies hold "#"
        /// local parameters at their defaults forever, so a condition the wearer's live values
        /// keep false can sit permanently true for everyone else.
        ///
        /// A transition conditioned on a Trigger is the one legitimate re-entry idiom — fire
        /// once per pulse — and is left alone. The CCK's own protected layers are not touched.
        /// </summary>
        static void SuppressAnyStateSelfRestarts(AnimatorController master, BridgeContext ctx)
        {
            var triggers = new HashSet<string>(master.parameters
                .Where(p => p.type == AnimatorControllerParameterType.Trigger)
                .Select(p => p.name));
            int flipped = 0;
            foreach (var layer in master.layers)
            {
                if (layer == null || layer.stateMachine == null || IsProtectedLayer(layer.name))
                {
                    continue;
                }
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var transition in machine.anyStateTransitions)
                    {
                        if (transition == null || !transition.canTransitionToSelf
                            || transition.conditions.Any(c => triggers.Contains(c.parameter)))
                        {
                            continue;
                        }
                        // Only states that HOLD STILL lose the flag. The every-frame restart
                        // pins a state at its first frame — and for a state with a real,
                        // animated clip, that pin IS the behaviour the avatar shipped with:
                        // its author saw frame 0 held in VRChat, however the clip's later
                        // frames look. The first version of this pass cleared the flag
                        // everywhere, and every multi-frame toggle animation on the next
                        // avatar started actually PLAYING — looping grow and hue cycles the
                        // author never saw. The strobe this pass exists to kill only ever
                        // came from states whose motion is constant or conversion-added
                        // (VRChat kept them empty; the filler is what restarts visibly), and
                        // for those the pin and the play are identical — so only those
                        // change.
                        var destination = transition.destinationState;
                        if (destination != null && !MotionHoldsStill(destination.motion))
                        {
                            continue;
                        }
                        // A destination with an exit-time transition DEPENDS on the restart to
                        // stay alive: every re-entry resets the clock, so the timed exit never
                        // fires — that is the whole mechanism holding the state. Clear the flag
                        // and the state plays through, the timed exit fires, and the same
                        // AnyState re-enters it from outside: a visible on/off cycle at clip
                        // length. One avatar's every clothing toggle was built this way
                        // (AnyState toSelf in, exit-time out), and 3.5.6 set them all cycling.
                        if (destination != null
                            && destination.transitions.Any(t => t != null && t.hasExitTime))
                        {
                            continue;
                        }
                        transition.canTransitionToSelf = false;
                        EditorUtility.SetDirty(transition);
                        flipped++;
                    }
                });
            }
            if (flipped == 0)
            {
                return;
            }
            EditorUtility.SetDirty(master);
            ctx.Report.Converted(Category,
                $"{flipped} AnyState transition(s) stopped restarting their own state every frame",
                "These carried Unity's default \"Can Transition To Self\", which with ordinary " +
                "conditions means the destination re-enters EVERY FRAME the conditions hold, " +
                "restarting its animation each time. VRChat hid it — the states involved were " +
                "empty there — but conversion must fill empty states, and a filled state restarted " +
                "every frame strobes: animations rapidly flicker, often only on OTHER players' " +
                "screens, because remote copies hold \"#\" local parameters at defaults that can " +
                "keep such a condition permanently true. Only still-holding states change — a " +
                "state with a real animated clip keeps the flag AND the held-at-first-frame look " +
                "its author shipped with. Transitions conditioned on a Trigger keep the flag too; " +
                "pulse-retriggering is the one thing it is for.");
        }

        /// <summary>
        /// Whether a motion never visibly changes while it plays: no motion at all, a clip whose
        /// every curve holds one value, or a tree of such clips. For these, being restarted every
        /// frame and playing through are the same picture — so the self-restart suppressor can
        /// act on them without changing anything an author ever saw.
        /// </summary>
        static bool MotionHoldsStill(Motion motion)
        {
            if (motion == null)
            {
                return true; // empty in VRChat; the filler this pass protects against comes later
            }
            if (motion is BlendTree tree)
            {
                return tree.children.All(child => MotionHoldsStill(child.motion));
            }
            if (!(motion is AnimationClip clip))
            {
                return false;
            }
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.keys.Length < 2)
                {
                    continue;
                }
                float first = curve.keys[0].value;
                for (int i = 1; i < curve.keys.Length; i++)
                {
                    if (Mathf.Abs(curve.keys[i].value - first) > 1e-5f)
                    {
                        return false;
                    }
                }
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keys == null || keys.Length < 2)
                {
                    continue;
                }
                for (int i = 1; i < keys.Length; i++)
                {
                    if (keys[i].value != keys[0].value)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Makes VRChat's avatar-scale family live: ScaleFactor, ScaleFactorInverse,
        /// EyeHeightAsPercent and ScaleModified, derived from EyeHeightAsMeters — which
        /// CreateParameterStreams feeds from ChilloutVR's AvatarHeight, the calibrated avatar
        /// height in metres that AvatarUpright divides by.
        ///
        /// The baseline ("what scale 1.0 means") is the avatar's viewpoint height at conversion
        /// time — the same number VRChat uses for its default scale. Every derivation is exact
        /// arithmetic run by a CCK AnimatorDriver each cycle:
        ///
        ///   ScaleFactor        = EyeHeightAsMeters / baseline
        ///   ScaleFactorInverse = baseline / EyeHeightAsMeters
        ///   EyeHeightAsPercent = (EyeHeightAsMeters − 0.2) / 4.8   (VRChat's 0.2–5 m range)
        ///   ScaleModified      = |ScaleFactor − 1| &gt; 1 %
        ///
        /// Derived values are kept local ("#") and recomputed per client — the input syncs, the
        /// arithmetic is deterministic, so every viewer computes identical values at zero extra
        /// sync cost, exactly the FeedVelocityMagnitude trade. Only runs when the avatar
        /// actually references one of the four; EyeHeightAsMeters alone needs no driver.
        /// </summary>
        static void FeedScaleParameters(AnimatorController master, BridgeContext ctx)
        {
            var derived = new[] { "ScaleFactor", "ScaleFactorInverse", "EyeHeightAsPercent", "ScaleModified" };
            var present = new Dictionary<string, AnimatorControllerParameter>();
            foreach (var p in master.parameters)
            {
                string bare = p.name.TrimStart('#');
                if (derived.Contains(bare) && !present.ContainsKey(bare))
                {
                    present[bare] = p;
                }
            }
            if (present.Count == 0)
            {
                return;
            }

            float baseline = ctx.CvrAvatar != null ? ctx.CvrAvatar.viewPosition.y : 0f;
            if (baseline < 0.05f)
            {
                baseline = 1.6f; // no believable viewpoint to measure against; VRChat's default
            }

            var parameters = master.parameters.ToList();
            void Ensure(string name)
            {
                if (parameters.All(p => p.name != name))
                {
                    parameters.Add(new AnimatorControllerParameter
                    {
                        name = name,
                        type = AnimatorControllerParameterType.Float,
                        // The resting value, so the first frames before the stream fires read
                        // "unscaled" rather than "0.05 m tall".
                        defaultFloat = baseline
                    });
                }
            }
            const string Factor = "#ScaleFactorCalc";
            const string Delta = "#ScaleDeltaCalc";
            const string Shift = "#EyeHeightShiftCalc";
            Ensure("EyeHeightAsMeters");
            master.parameters = AppendScratch(parameters.ToArray(), Factor);

            AnimatorDriverTask Task(string targetName, AnimatorDriverTask.ParameterType targetType,
                AnimatorDriverTask.Operator op,
                string aParam, float aStatic, string bParam, float bStatic)
            {
                var task = new AnimatorDriverTask
                {
                    targetType = targetType,
                    targetName = targetName,
                    op = op
                };
                if (aParam != null)
                {
                    task.aType = AnimatorDriverTask.SourceType.Parameter;
                    task.aParamType = AnimatorDriverTask.ParameterType.Float;
                    task.aName = aParam;
                }
                else
                {
                    task.aType = AnimatorDriverTask.SourceType.Static;
                    task.aValue = aStatic;
                }
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

            var tick = new AnimationClip { name = "Scale Feed Tick" };
            tick.SetCurve("", typeof(Animator), Factor + "Tick", AnimationCurve.Constant(0f, 1f / 60f, 0f));

            var machine = new AnimatorStateMachine
            {
                name = "Scale Feed",
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
            driver.localOnly = false; // remotes recompute from the synced EyeHeightAsMeters
            var tasks = driver.EnterTasks;
            var Float = AnimatorDriverTask.ParameterType.Float;
            tasks.Add(Task(Factor, Float, AnimatorDriverTask.Operator.Division,
                "EyeHeightAsMeters", 0f, null, baseline));
            if (present.TryGetValue("ScaleFactor", out var scaleFactor))
            {
                tasks.Add(Task(scaleFactor.name, Float, AnimatorDriverTask.Operator.Set,
                    Factor, 0f, null, 0f));
            }
            if (present.TryGetValue("ScaleFactorInverse", out var inverse))
            {
                tasks.Add(Task(inverse.name, Float, AnimatorDriverTask.Operator.Division,
                    null, baseline, "EyeHeightAsMeters", 0f));
            }
            if (present.TryGetValue("EyeHeightAsPercent", out var percent))
            {
                master.parameters = AppendScratch(master.parameters, Shift);
                tasks.Add(Task(Shift, Float, AnimatorDriverTask.Operator.Subtraction,
                    "EyeHeightAsMeters", 0f, null, 0.2f));
                tasks.Add(Task(percent.name, Float, AnimatorDriverTask.Operator.Division,
                    Shift, 0f, null, 4.8f));
            }
            if (present.TryGetValue("ScaleModified", out var modified))
            {
                master.parameters = AppendScratch(master.parameters, Delta);
                // Squared distance from 1, compared against (1 %)² — an exact-equality check
                // on a streamed float would flicker.
                tasks.Add(Task(Delta, Float, AnimatorDriverTask.Operator.Subtraction,
                    Factor, 0f, null, 1f));
                tasks.Add(Task(Delta, Float, AnimatorDriverTask.Operator.Multiplication,
                    Delta, 0f, Delta, 0f));
                tasks.Add(Task(modified.name,
                    modified.type == AnimatorControllerParameterType.Bool
                        ? AnimatorDriverTask.ParameterType.Bool
                        : Float,
                    AnimatorDriverTask.Operator.MoreThan,
                    Delta, 0f, null, 0.0001f));
            }

            var layers = master.layers.ToList();
            layers.Add(new AnimatorControllerLayer
            {
                name = "Scale Feed",
                defaultWeight = 1f,
                stateMachine = machine
            });
            master.layers = layers.ToArray();

            ctx.Report.Converted(Category,
                $"VRChat's avatar-scale parameters are live — {string.Join(", ", present.Keys)}",
                $"Derived from EyeHeightAsMeters, which a parameter stream feeds from ChilloutVR's " +
                $"calibrated avatar height, against this avatar's converted viewpoint height of " +
                $"{baseline:0.00} m as scale 1.0. A generated driver layer recomputes them each cycle, " +
                "locally on every client — the input syncs, the arithmetic is deterministic, so remote " +
                "viewers see the same values at no extra sync cost. ScaleModified flips at 1% off " +
                "baseline. Scale-reactive gimmicks (VRCFury scale detectors and similar) run on these.");
        }

        static AnimatorControllerParameter[] AppendScratch(AnimatorControllerParameter[] parameters, string name)
        {
            if (parameters.Any(p => p.name == name))
            {
                return parameters;
            }
            var list = parameters.ToList();
            list.Add(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 0f
            });
            return list.ToArray();
        }

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
                string target = OutputAssetPaths.Claim(
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
                string target = OutputAssetPaths.Claim(
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
            var maskedBaseBody = new SortedSet<string>(StableSampleOrder.Instance);

            foreach (var layer in layers)
            {
                if (!vrcNames.Contains(layer.name) || layer.avatarMask != null)
                {
                    continue;
                }
                InspectLayerCurves(layer, out bool body, out bool fingers);
                // A [Base] layer is masked even when it DOES animate the body, which is the one
                // exception to "deliberate body animation is left alone".
                //
                // In VRChat the Base playable is the bottom of the stack — the thing that provides
                // the body pose. ChilloutVR's equivalent is its own Locomotion/Emotes layer, and
                // that layer is not optional: the stance buttons and movement sliders are answered
                // there and nowhere else. A merged [Base] layer lands ABOVE it on Override at
                // weight 1, so it cannot supplement CVR's locomotion — only replace it, and only
                // with whatever VRChat's Base happened to be doing.
                //
                // That is rarely locomotion. On the avatar that forced this, the [Base] layer was
                // a calibration utility whose three states are "measure me", "Preview" and
                // "reinitialize"; unmasked at weight 1 it held the body in a measurement pose,
                // movement animated nothing, and Airborne/Flying/Sitting/Swimming did nothing
                // because the layer answering them had been overridden. Genuine locomotion
                // REPLACEMENTS fare no better: they lean on runtime layer-weight control, which
                // ChilloutVR has no equivalent for, so they cannot run here either way.
                //
                // Masked, the layer keeps everything it can actually deliver — object toggles,
                // blendshapes, materials, parameters, additive floating — and CVR's locomotion
                // stays authoritative. Enabling "Base / locomotion" can no longer break an avatar.
                bool baseLayer = layer.name.StartsWith("[Base]", StringComparison.Ordinal);
                if (body && !baseLayer)
                {
                    continue; // deliberate body animation; reported separately
                }
                if (body)
                {
                    maskedBaseBody.Add(layer.name);
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

            if (maskedBaseBody.Count > 0)
            {
                ctx.Report.Approximated(Category,
                    $"{maskedBaseBody.Count} \"Base / locomotion\" layer(s) blocked from driving the body",
                    $"{string.Join(", ", maskedBaseBody)} — these animate humanoid muscles, and merged into one " +
                    "ChilloutVR controller they land ABOVE the client's own Locomotion/Emotes layer on Override " +
                    "at full weight. They cannot add to CVR's locomotion from there, only replace it — and CVR's " +
                    "layer is where the movement sliders and the Airborne / Flying / Sitting / Swimming stances " +
                    "are answered, so letting them through costs you all of it. What VRChat put in Base is " +
                    "usually not locomotion anyway (one avatar's was a calibration utility that simply held the " +
                    "body still), and true locomotion REPLACEMENTS depend on runtime layer-weight control that " +
                    "ChilloutVR has no equivalent for, so they cannot run here regardless. Everything else in " +
                    "these layers is untouched: object toggles, blendshapes, materials, parameters and additive " +
                    "motion all still convert. If you specifically want one driving your body, clear its Mask in " +
                    "the Animator window — and expect the stances to stop responding.");
            }
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
        internal static void AuditHandPoseConflictsForTest(AnimatorController master, BridgeContext ctx)
            => AuditHandPoseConflicts(master, ctx);

        static void AuditHandPoseConflicts(AnimatorController master, BridgeContext ctx)
        {
            // This used to return early when the gesture layer was being converted, reasoning that
            // "the avatar's own gesture layers ARE the hand pose, so there is nothing to protect".
            // That confuses the SOURCE of the pose with its SAFETY. The promoted LeftHand/RightHand
            // layers are indeed the avatar's own — and something merged in above them can still
            // overwrite the fingers they just posed.
            //
            // Reported from the wild on two avatars whose FX layer ALSO carried layers called
            // "Left Hand" and "Right Hand". Both copies survived: the promoted pair at 2 and 3, the
            // FX duplicates at 5 and 6, all unmasked and all at weight 1, so the FX pair won. On one
            // of them the winning copy had no Idle state at all and a fist band starting at -0.9,
            // which parks the hand in a fist at rest — reported as "gestures are just wrong, with
            // the wrong thresholds". The promoted layer's own bands were correct throughout.
            //
            // It cannot happen in VRChat, which is why an avatar can ship like this and look fine:
            // the FX playable layer there cannot drive humanoid muscles at all, so those FX hand
            // layers never touched a finger. Merging everything into one controller hands them
            // muscles they never had.
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
                if (layer.defaultWeight <= 0f || layer.blendingMode != AnimatorLayerBlendingMode.Override)
                {
                    continue;
                }
                if (mask != null
                    && !mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers)
                    && !mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers))
                {
                    continue; // already cannot reach fingers
                }
                InspectLayerCurves(layer, out bool body, out bool fingers);
                if (body)
                {
                    // A layer that deliberately drives the body — a kept GoGo locomotion
                    // replacement, say. Silently stripping its fingers would be us overruling
                    // the author, so this one is the user's call.
                    warned.Add(layer.name);
                    continue;
                }
                if (mask == null)
                {
                    // No mask at all, so nothing to edit — and this is the case that mattered.
                    // Only act when the layer really animates fingers: giving a mask to a layer
                    // that has none is a bigger intervention than editing one, and for a layer
                    // with no finger curves it would change nothing anyway.
                    if (!fingers)
                    {
                        continue;
                    }
                    layer.avatarMask = GetNoFingersMask(ctx);
                    repaired.Add(layer.name);
                    changed = true;
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
                    "above the LeftHand/RightHand layers and could write finger muscles, which on " +
                    "Override at full weight replaces whatever pose the gesture just played. Fingers " +
                    "are now masked off them; everything else those layers animate is untouched. " +
                    "This is what makes a gesture look \"dead\" in game while the CCK Debugger shows " +
                    "the right clip playing at weight 1 — and when the offender is a COPY of the " +
                    "avatar's own hand layers that VRChat kept in its FX playable, the symptom is " +
                    "stranger still: gestures that work but land on the wrong pose, or a hand stuck " +
                    "in a fist at rest, because the copy that won was never the one driving fingers " +
                    "in VRChat. FX cannot touch humanoid muscles there; merged into one ChilloutVR " +
                    "controller it can.");
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

        /// <summary>
        /// Why the empty-state filler did what it did, per layer, for the coverage checker to
        /// print beside a violation. A violation without its refusal reason cost three
        /// reconversion round-trips on one avatar; with it, the fix is named in the report.
        /// </summary>
        static readonly Dictionary<string, string> restoreVerdicts = new Dictionary<string, string>();

        /// <summary>
        /// Gives every empty "off" state a real clip that restores what its layer animates.
        ///
        /// The VRChat idiom for a toggle is two states: one holding the clip that changes
        /// something, and one holding NOTHING, whose job is to put it back. That empty state
        /// only works because Write Defaults writes each property's captured default, and the
        /// avatar arrives relying on it — which is why the source plays correctly in VRChat and
        /// in Gesture Manager.
        ///
        /// Converted, the same layer can turn a toggle ON and never off again: the "off" state
        /// has no animation in it, so there is nothing to undo the change. Every toggle on the
        /// avatar behaves the same way, because they are all built the same way.
        ///
        /// Rather than depend on Write Defaults behaving identically on both platforms, the
        /// default is MEASURED off the converted avatar and baked into a clip. Whatever the
        /// property is at conversion time — the object active, the blendshape at 0, the material
        /// as authored — becomes an explicit curve, so the off state restores it by playing
        /// animation rather than by relying on an implicit rule.
        ///
        /// Only properties the layer's own other states animate are restored, so a state stays
        /// silent about everything it was already silent about, and layers keep their
        /// independence.
        /// </summary>
        /// <param name="writtenPaths">
        /// Deterministic file names, so reconverting REPLACES last time's restore clips instead of
        /// parking a numbered copy beside them. One avatar had 200 of them.
        ///
        /// SHARED with the blend tree pass rather than local, because both write " restore" clips
        /// into one folder and both name them after the thing they restore. A nativized toggle
        /// layer and the tree Fury built from that same toggle carry the same name often enough
        /// that the second pass would otherwise overwrite the first pass's clip.
        /// </param>
        static void FillEmptyStatesWithRestoreClips(AnimatorController master, BridgeContext ctx,
            HashSet<string> writtenPaths)
        {
            var root = ctx.Target.transform;
            string dir = $"{ctx.OutputDir}/RehomedAssets";
            restoreVerdicts.Clear();
            int filled = 0, layersTouched = 0, reused = 0, sharedSkipped = 0, candidates = 0, routers = 0;
            // Bindings that named something this avatar no longer has. Counted because the two
            // skips below used to be silent, and when EVERY binding took one of them the pass
            // reported "found candidates and produced nothing" with no way to tell which — see
            // the failure branch at the bottom, where that question is finally answerable.
            int unresolved = 0;
            var unresolvedPaths = new SortedSet<string>(StableSampleOrder.Instance);
            var names = new List<string>();
            var keptClips = new HashSet<string>();
            // Layers whose empty states are structural rather than a toggle.s off half.
            var notToggles = new SortedSet<string>(StableSampleOrder.Instance);

            // Snapshot ONCE. master.layers hands back a fresh array of fresh wrappers on every
            // access, so an index looked up against one call is meaningless against another —
            // Array.IndexOf(master.layers, layer) never matches and quietly returns -1, which
            // is how an earlier revision decided no layer owned anything and generated nothing
            // at all. Layer names are unique by construction (MakeUniqueLayerName), so they are
            // the identity to key on.
            var layers = master.layers;
            var indexByName = new Dictionary<string, int>();
            for (int i = 0; i < layers.Length; i++)
            {
                if (!indexByName.ContainsKey(layers[i].name))
                {
                    indexByName[layers[i].name] = i;
                }
            }
            BuildRestoreOwnership(layers, out var owner, out var allClips, out var treeDriven);

            // Every layer of the finished controller except the ones that must not be touched —
            // NOT the merged-layer list. ToggleNativizer takes a toggle's layer OUT of that list
            // when it gives it its own name, so keying on it silently skipped exactly the layers
            // most likely to need this: six plain wardrobe toggles, four of them reported broken.
            // A layer is still the avatar's toggle after it has been renamed.
            foreach (var layer in layers)
            {
                if (IsProtectedLayer(layer.name))
                {
                    continue;
                }
                // What this layer animates, and which of its states have nothing at all.
                var bindings = new HashSet<EditorCurveBinding>();
                var objectBindings = new HashSet<EditorCurveBinding>();
                var empties = new List<AnimatorState>();
                int stateCount = 0;
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        stateCount++;
                        var state = child.state;
                        if (state == null)
                        {
                            continue;
                        }
                        // A curve-less clip IS an empty state. VRCFury does not leave its empty
                        // toggle halves null — it parks a shared clip with no curves in them —
                        // and the tree collector has accepted that spelling since the day it was
                        // written, while this one demanded null. On a Fury avatar whose toggles
                        // are state pairs, that asymmetry made every single one invisible here:
                        // 'found candidates and produced nothing' with the candidates being only
                        // the states some later strip had genuinely nulled.
                        if (state.motion == null || IsCurveless(state.motion, null))
                        {
                            // An empty state is only an "off" state if the layer can REST in it.
                            // A router can't be rested in and must not be given values to assert.
                            if (!IsPassThroughState(state))
                            {
                                empties.Add(state);
                            }
                            else
                            {
                                routers++;
                            }
                            continue;
                        }
                        CollectBindings(state.motion, bindings, objectBindings);
                    }
                });
                if (empties.Count == 0 || (bindings.Count == 0 && objectBindings.Count == 0))
                {
                    restoreVerdicts[layer.name] = empties.Count == 0
                        ? "no empty state to fill (every state already holds a motion, or is a router)"
                        : "empty state found, but the layer animates nothing to restore";
                    continue;
                }
                // ONLY the two-state toggle. VRChat's idiom is exactly one empty "off" state and
                // one state holding the clip, and that shape is the only one where a snapshot of
                // the avatar is the right thing to put in the empty half.
                //
                // Anything larger is a machine, and its empty states are structural. Two have now
                // shipped as bugs: a local/remote gate given the values of the branch it leads to,
                // and a slider layer's "Reset/Pause" state given a snapshot that pinned seven chest
                // blendshapes to 0 — which flattened the avatar's chest the moment the layer rested
                // there. Neither was an "off" state; both merely had no motion.
                //
                // Erring toward doing nothing is cheap here: an unfilled off state behaves exactly
                // as it did in VRChat, which is the situation this pass improves on rather than
                // rescues. Erring the other way changes what the avatar looks like.
                if (stateCount != 2)
                {
                    restoreVerdicts[layer.name] = $"not a two-state toggle ({stateCount} states)";
                    notToggles.Add($"{layer.name} ({stateCount} states)");
                    continue;
                }
                candidates += empties.Count;

                var clip = new AnimationClip { name = SanitizeFileName($"{layer.name} restore") };
                int curves = 0, shared = 0;

                // Where several layers animate one property, only the LOWEST of them may restore
                // it. Depth decides, not exclusivity.
                //
                // A dress toggle and a shirt toggle both animate the shirt object. Let both
                // restore it and the higher one — the dress — asserts "shirt on" every frame and
                // the shirt can never be switched off. Let neither restore it and the shirt can
                // never be switched back on. Give it to the lower layer and every combination is
                // right: the shirt layer restores the shirt, and the dress layer stays silent, so
                // when the dress is ON its own clip still wins from above, and when it is off the
                // shirt layer decides. Silence at the top, authority at the bottom.
                int here = indexByName.TryGetValue(layer.name, out int found) ? found : -1;
                bool Owns(EditorCurveBinding binding)
                {
                    // A blend tree drives this somewhere: leave it to the tree. A constant
                    // assertion from any plain state fights a parameter-driven value.
                    if (treeDriven.Contains(binding))
                    {
                        return false;
                    }
                    // Unknown binding: nobody else claims it, so this layer may restore it.
                    return !owner.TryGetValue(binding, out int lowest) || lowest == here;
                }

                foreach (var binding in bindings)
                {
                    // Humanoid muscles are masked off these layers anyway, and baking a muscle
                    // default would fight locomotion for the whole session.
                    if (binding.type == typeof(Animator))
                    {
                        continue;
                    }
                    if (!Owns(binding))
                    {
                        shared++;
                        continue;
                    }
                    if (!AnimationUtility.GetFloatValue(ctx.Target, binding, out float value))
                    {
                        // The clip names an object or property the converted avatar does not have
                        // — stripped with a VRChat-only system, or renamed by a baker. There is no
                        // current value to snapshot, so nothing can be restored here.
                        unresolved++;
                        unresolvedPaths.Add($"{binding.path}:{binding.propertyName}");
                        continue;
                    }
                    AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 0f, value));
                    curves++;
                }
                foreach (var binding in objectBindings)
                {
                    if (!Owns(binding))
                    {
                        shared++;
                        continue;
                    }
                    if (!AnimationUtility.GetObjectReferenceValue(ctx.Target, binding, out var value))
                    {
                        unresolved++;
                        unresolvedPaths.Add($"{binding.path}:{binding.propertyName}");
                        continue;
                    }
                    AnimationUtility.SetObjectReferenceCurve(clip, binding,
                        new[] { new ObjectReferenceKeyframe { time = 0f, value = value } });
                    curves++;
                }
                sharedSkipped += shared;
                if (curves == 0)
                {
                    restoreVerdicts[layer.name] =
                        $"candidate refused in full: {shared} propert(ies) owned elsewhere or " +
                        "tree-driven, the rest unresolved or parameter-typed";
                    UnityEngine.Object.DestroyImmediate(clip);
                    continue;
                }
                restoreVerdicts[layer.name] = $"filled ({curves} curve(s))"
                    + (shared > 0 ? $", {shared} refused as owned elsewhere or tree-driven" : "");

                // Prefer the avatar's OWN animation. If the author already ships a clip that
                // sets these same properties to these same values — the "on" half of a pair
                // that was simply never wired into the empty state — use theirs. A generated
                // clip is a last resort, not a default: theirs may carry curves and timing this
                // snapshot cannot know about, and one asset is better than two that agree.
                var existing = FindEquivalentClip(clip, allClips);
                if (existing != null)
                {
                    UnityEngine.Object.DestroyImmediate(clip);
                    foreach (var state in empties)
                    {
                        state.motion = existing;
                        filled++;
                    }
                    layersTouched++;
                    restoreVerdicts[layer.name] = $"filled by reusing the avatar's own \"{existing.name}\"";
                    names.Add($"{layer.name} (reused \"{existing.name}\")");
                    reused++;
                    continue;
                }

                if (!AssetDatabase.IsValidFolder(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                    AssetDatabase.Refresh();
                }
                // A stable name per layer, uniquified only against THIS run. GenerateUniqueAssetPath
                // uniquifies against the folder, so every reconversion parked another numbered copy
                // next to the last — one avatar's output folder had reached "restore 9".
                string path = $"{dir}/{clip.name}.anim";
                for (int n = 2; !writtenPaths.Add(path); n++)
                {
                    path = $"{dir}/{clip.name} {n}.anim";
                }
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }
                AssetDatabase.CreateAsset(clip, path);
                keptClips.Add(path);
                foreach (var state in empties)
                {
                    state.motion = clip;
                    filled++;
                }
                layersTouched++;
                names.Add(layer.name);
            }

            if (filled == 0)
            {
                // Candidates but no fills is not a quiet "nothing to do" — it is the shape of a
                // bug in this pass, and one that already shipped once by saying nothing at all.
                // A toggle that switches on and never off is the visible symptom, so say it here
                // rather than leave the report silent about a pass that ran and achieved zero.
                if (candidates > 0)
                {
                    // Say WHY, which this used to leave out entirely — it counted the candidates,
                    // asked to be told about it, and threw away the two numbers that answer the
                    // question. Reported from an avatar whose seven toggles all switched on and
                    // stayed on, where the report could only say that something had gone wrong.
                    string because =
                        unresolved > 0 && sharedSkipped > 0
                            ? $" {unresolved} propert(ies) named something this avatar no longer has, and " +
                              $"{sharedSkipped} belong to a lower layer, so nothing was left to restore."
                        : unresolved > 0
                            ? $" Every property they animate — {unresolved} of them — names something this " +
                              "avatar no longer has, so there was no current value to snapshot. Removing a " +
                              "VRChat-only system takes its objects with it, and a toggle that only ever " +
                              "moved those has nothing left to restore: " +
                              $"{string.Join(", ", unresolvedPaths.Take(4))}" +
                              (unresolvedPaths.Count > 4 ? $", … ({unresolvedPaths.Count} paths)" : "") + "."
                        : sharedSkipped > 0
                            ? $" All {sharedSkipped} of the properties they animate are claimed by a lower " +
                              "layer, which restores them instead — see the note about several layers " +
                              "animating one thing. If these toggles do stick, that rule is picking the " +
                              "wrong layer and the conversion is worth reporting."
                            : " The pass found candidates and produced nothing, with no property either " +
                              "unresolved or claimed elsewhere — which it should not do. Please report " +
                              "this conversion.";

                    ctx.Report.Warning(Category,
                        $"{candidates} empty \"off\" state(s) were left without a restore animation",
                        "Each belongs to a toggle whose off direction now depends on Write Defaults " +
                        "putting the property back. If any of these switch on and never off again, " +
                        "that is why." + because);
                }
                return;
            }
            int stale = DeleteStaleRestoreClips(dir, keptClips);
            AssetDatabase.SaveAssets();
            ctx.Report.Converted(Category,
                $"{filled} empty \"off\" state(s) across {layersTouched} layer(s) given a restore animation",
                $"{string.Join(", ", names.Take(6))}{(names.Count > 6 ? ", …" : "")}" +
                (reused > 0
                    ? $" — {reused} of them reuse an animation the avatar ALREADY had, which is " +
                      "preferred wherever one matches; the rest were generated. "
                    : " — ") +
                "VRChat's toggle " +
                "idiom leaves the off state EMPTY and lets Write Defaults put the property back. That " +
                "makes the off direction depend on an implicit rule rather than on animation, and a " +
                "toggle built that way can switch on and never off again. Each off state now plays a " +
                "clip holding the value the property has on this avatar right now — object active, " +
                "blendshape at rest, material as authored — so it restores by animating, the same on " +
                "any platform. Only properties its own layer animates are touched. If a toggle should " +
                "rest in its OTHER position, set that up on the avatar before converting: whatever is " +
                "true at conversion time is what \"off\" now means." +
                (sharedSkipped > 0
                    ? $" {sharedSkipped} propert(ies) were left to a lower layer: where several layers " +
                      "animate one thing, only the lowest restores it. A dress toggle and a shirt " +
                      "toggle that both move the shirt is the usual case — if the dress layer restored " +
                      "the shirt it would assert it from above and the shirt could never be taken off, " +
                      "and if neither did it could never be put back on. The lower layer owns it, the " +
                      "higher one stays silent, and both toggles work."
                    : "") +
                (routers > 0
                    ? $" {routers} empty state(s) were left empty because the layer only passes " +
                      "THROUGH them: their transitions cover every value of a parameter, so the layer " +
                      "can never come to rest there. The local/remote gate VRChat avatars use is the " +
                      "usual one, and it is empty deliberately — giving it values to hold would make " +
                      "it assert them for as long as the layer sat there."
                    : "") +
                (stale > 0
                    ? $" {stale} restore clip(s) from a previous conversion of this avatar were deleted; " +
                      "they are regenerated every time and used to pile up beside each other."
                    : "") +
                (notToggles.Count > 0
                    ? $"\n\nLeft alone, not a two-state toggle ({notToggles.Count}): " +
                      $"{string.Join(", ", notToggles.Take(6))}{(notToggles.Count > 6 ? ", …" : "")}. " +
                      "VRChat's idiom is exactly one empty \"off\" state and one holding the clip, and " +
                      "that is the only shape where a snapshot of the avatar belongs in the empty half. " +
                      "Bigger layers are machines whose empty states are structural — a slider's " +
                      "reset/pause, a local/remote gate — and filling those changes how the avatar looks. " +
                      "They behave exactly as they did in VRChat."
                    : ""));
        }

        /// <summary>
        /// Which layer OWNS each animated property, and every clip the controller already holds.
        ///
        /// Ownership is by DEPTH: the lowest layer index that animates a property owns it, and only
        /// the owner may restore it. Shared by both restore passes — the animator-state one and the
        /// blend tree one — because a property written from a state in one layer and from a tree in
        /// another layer poses exactly the same conflict, and answering it two different ways would
        /// let both of them restore it.
        /// </summary>
        static void BuildRestoreOwnership(AnimatorControllerLayer[] layers,
            out Dictionary<EditorCurveBinding, int> owner, out HashSet<AnimationClip> allClips,
            out HashSet<EditorCurveBinding> treeDriven)
        {
            // Locals rather than the out params directly: a lambda may not touch an out parameter,
            // and the walk below is a lambda.
            var owners = new Dictionary<EditorCurveBinding, int>();
            // Every clip the avatar already has, so an authored one can be preferred over a
            // generated one.
            var clips = new HashSet<AnimationClip>();
            // Bindings that any BLEND TREE anywhere animates. Ownership is awarded from plain
            // clip states only, and tree-driven bindings are exempt from state restores entirely:
            // a tree's curves are parameter-driven and continuously blended, so a constant
            // assertion from any plain state — above OR below — fights the tree whenever it is
            // live. The measured case: a low gesture layer's weight trees touched hundreds of
            // bindings, claimed ownership of all of them, was rightly refused permission to
            // assert them (the slider rule), and thereby orphaned every wardrobe toggle above it
            // — nobody restored anything, and the whole avatar was one-way in game.
            var trees = new HashSet<EditorCurveBinding>();
            int layerIndex = -1;
            foreach (var candidate in layers)
            {
                layerIndex++;
                var floatsHere = new HashSet<EditorCurveBinding>();
                var objectsHere = new HashSet<EditorCurveBinding>();
                // Ownership only counts states Unity can actually reach. A library layer (see
                // LibraryDefaultState) claimed a whole wardrobe on a real avatar: 75 orphan
                // states at layer 4, each holding one toggle's clip, awarded every property to
                // a layer that can never play — so every live toggle above it was refused as
                // "owned by a lower layer", the library restored nothing, and every toggle on
                // the avatar switched off and never back on in game. Clips are still collected
                // from everywhere: a library is exactly where an authored clip worth reusing
                // by FindEquivalentClip lives.
                var onlyPlayable = LibraryDefaultState(candidate);
                WalkMachines(candidate.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        var motion = child.state != null ? child.state.motion : null;
                        CollectClips(motion, clips);
                        if (motion is BlendTree)
                        {
                            // Trees feed the exemption set, never ownership — see above.
                            CollectBindings(motion, trees, trees);
                            continue;
                        }
                        if (onlyPlayable == null || child.state == onlyPlayable)
                        {
                            CollectBindings(motion, floatsHere, objectsHere);
                        }
                    }
                });
                foreach (var binding in floatsHere.Concat(objectsHere))
                {
                    if (!owners.ContainsKey(binding))
                    {
                        owners[binding] = layerIndex;
                    }
                }
            }
            owner = owners;
            allClips = clips;
            treeDriven = trees;
        }

        /// <summary>
        /// The single state a layer can ever play, or null when the layer genuinely runs.
        ///
        /// A machine holding several states and not one transition anywhere — no state-to-state,
        /// no AnyState, no Entry, none in any sub-machine — can only rest in its default state
        /// forever. The rest is an animation LIBRARY: a place authors park clips so they are easy
        /// to find and preview, common enough in VRChat FX controllers that one real avatar
        /// shipped 75 of its toggles' clips this way. Unity cannot enter those states, so nothing
        /// they animate can ever fight anything at runtime — which is exactly why they must not
        /// take part in deciding which layer restores a property.
        ///
        /// Deliberately this narrow: zero transitions in the whole layer. A layer with even one
        /// transition somewhere gets full ownership of everything it animates, reachable or not,
        /// because partial reachability needs real graph analysis through entry/exit semantics and
        /// a wrong answer here silently re-breaks toggles. Widen it only on evidence.
        /// </summary>
        static AnimatorState LibraryDefaultState(AnimatorControllerLayer layer)
        {
            var root = layer != null ? layer.stateMachine : null;
            if (root == null)
            {
                return null;
            }
            int states = 0, transitions = 0;
            WalkMachines(root, machine =>
            {
                states += machine.states.Length;
                transitions += machine.anyStateTransitions.Length + machine.entryTransitions.Length;
                foreach (var child in machine.states)
                {
                    if (child.state != null)
                    {
                        transitions += child.state.transitions.Length;
                    }
                }
                foreach (var sub in machine.stateMachines)
                {
                    if (sub.stateMachine != null)
                    {
                        transitions += machine.GetStateMachineTransitions(sub.stateMachine).Length;
                    }
                }
            });
            return states > 1 && transitions == 0 ? root.defaultState : null;
        }

        /// <summary>A 1D blend tree shaped like a toggle: one child animates, the other is empty.</summary>
        struct ToggleTree
        {
            public BlendTree Tree;
            public int EmptyIndex;
            public HashSet<EditorCurveBinding> Floats;
            public HashSet<EditorCurveBinding> Objects;
        }

        /// <summary>
        /// The blend tree half of the off-state restore, for toggles VRCFury turned into trees.
        ///
        /// FillEmptyStatesWithRestoreClips repairs the toggle whose off half is an empty animator
        /// STATE. VRCFury's LayerToTreeService rewrites whole toggle layers into 1D blend trees
        /// nested under one Direct tree, and then the off half is an empty CHILD instead — the same
        /// idiom with the same defect, and invisible to a pass that only reads <c>state.motion</c>.
        /// Measured on the avatar that reported it: 53 of its 94 empty motion slots were tree
        /// children, its wardrobe toggles switched on and never off in game, and the state pass
        /// found exactly one layer to work on.
        ///
        /// The shape accepted here is the tree spelling of the two-state toggle and nothing else: a
        /// 1D tree, exactly two children, exactly one of which animates nothing. 1D trees NORMALISE,
        /// so the empty child plays at full weight when the parameter sits at its threshold — which
        /// is what makes a snapshot placed there restore the property rather than merely dilute it.
        /// Fury's Direct parent weights each toggle by a constant-1 parameter (Toggle_Weight), so
        /// the subtree runs at full strength.
        ///
        /// ONE EXTRA RULE the state pass does not need. A Direct tree SUMS its children rather than
        /// choosing between them, so two sibling toggles animating one property would fight the
        /// moment both assert: the toggle switched ON writes 0, the other's restore writes 1, and
        /// the sum reads as on. So a property is restored only where exactly ONE toggle in the layer
        /// animates it, and nothing else in that layer does. A wardrobe with an "all clothing off"
        /// preset overlapping four garment toggles is the ordinary case — those four keep VRChat's
        /// behaviour rather than put the preset at risk.
        /// </summary>
        static void FillEmptyTreeSlotsWithRestoreClips(AnimatorController master, BridgeContext ctx,
            HashSet<string> writtenPaths)
        {
            string dir = $"{ctx.OutputDir}/RehomedAssets";
            int filled = 0, reused = 0, candidateCount = 0;
            var names = new List<string>();
            var contested = new SortedSet<string>(StableSampleOrder.Instance);

            var layers = master.layers;
            var indexByName = new Dictionary<string, int>();
            for (int i = 0; i < layers.Length; i++)
            {
                if (!indexByName.ContainsKey(layers[i].name))
                {
                    indexByName[layers[i].name] = i;
                }
            }
            BuildRestoreOwnership(layers, out var owner, out var allClips, out _);

            foreach (var layer in layers)
            {
                if (IsProtectedLayer(layer.name))
                {
                    continue;
                }
                int here = indexByName.TryGetValue(layer.name, out int found) ? found : -1;

                var candidates = new List<ToggleTree>();
                var seen = new HashSet<BlendTree>();
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        if (child.state != null)
                        {
                            CollectToggleTrees(child.state.motion, seen, candidates);
                        }
                    }
                });
                if (candidates.Count == 0)
                {
                    continue;
                }
                candidateCount += candidates.Count;

                // How many toggles in this layer animate each property, and what the REST of the
                // layer animates outside them. Both have to say "only me" before anything moves.
                var usage = new Dictionary<EditorCurveBinding, int>();
                foreach (var candidate in candidates)
                {
                    foreach (var binding in candidate.Floats.Concat(candidate.Objects))
                    {
                        usage.TryGetValue(binding, out int n);
                        usage[binding] = n + 1;
                    }
                }
                var outside = new HashSet<EditorCurveBinding>();
                var toggleTrees = new HashSet<BlendTree>(candidates.Select(c => c.Tree));
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        if (child.state != null)
                        {
                            CollectOutsideToggles(child.state.motion, toggleTrees, outside);
                        }
                    }
                });

                foreach (var candidate in candidates)
                {
                    string label = ToggleTreeLabel(candidate.Tree, layer.name);
                    bool Owns(EditorCurveBinding binding)
                    {
                        if (usage.TryGetValue(binding, out int users) && users > 1)
                        {
                            return false;
                        }
                        if (outside.Contains(binding))
                        {
                            return false;
                        }
                        // Unknown binding: nobody else claims it, so this layer may restore it.
                        return !owner.TryGetValue(binding, out int lowest) || lowest == here;
                    }

                    var clip = new AnimationClip { name = SanitizeFileName($"{label} restore") };
                    int curves = 0, shared = 0;
                    foreach (var binding in candidate.Floats)
                    {
                        // Fury's AAP trees animate animator PARAMETERS rather than the avatar, and
                        // share this exact two-child shape. Snapshotting one would pin a value the
                        // math behind it exists to compute — and humanoid muscles are masked off
                        // these layers anyway.
                        if (binding.type == typeof(Animator))
                        {
                            continue;
                        }
                        if (!Owns(binding))
                        {
                            shared++;
                            continue;
                        }
                        if (!AnimationUtility.GetFloatValue(ctx.Target, binding, out float value))
                        {
                            continue;
                        }
                        AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 0f, value));
                        curves++;
                    }
                    foreach (var binding in candidate.Objects)
                    {
                        if (!Owns(binding))
                        {
                            shared++;
                            continue;
                        }
                        if (!AnimationUtility.GetObjectReferenceValue(ctx.Target, binding, out var value))
                        {
                            continue;
                        }
                        AnimationUtility.SetObjectReferenceCurve(clip, binding,
                            new[] { new ObjectReferenceKeyframe { time = 0f, value = value } });
                        curves++;
                    }
                    if (shared > 0)
                    {
                        contested.Add(label);
                    }
                    if (curves == 0)
                    {
                        UnityEngine.Object.DestroyImmediate(clip);
                        continue;
                    }

                    // Prefer the avatar's OWN animation over a generated one, exactly as the state
                    // pass does — theirs may carry curves and timing a snapshot cannot know about.
                    Motion restore;
                    var existing = FindEquivalentClip(clip, allClips);
                    if (existing != null)
                    {
                        UnityEngine.Object.DestroyImmediate(clip);
                        restore = existing;
                        reused++;
                    }
                    else
                    {
                        if (!AssetDatabase.IsValidFolder(dir))
                        {
                            System.IO.Directory.CreateDirectory(dir);
                            AssetDatabase.Refresh();
                        }
                        string path = $"{dir}/{clip.name}.anim";
                        for (int n = 2; !writtenPaths.Add(path); n++)
                        {
                            path = $"{dir}/{clip.name} {n}.anim";
                        }
                        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null)
                        {
                            AssetDatabase.DeleteAsset(path);
                        }
                        AssetDatabase.CreateAsset(clip, path);
                        restore = clip;
                    }

                    // children is a copy; the setter is what writes it back.
                    var kids = candidate.Tree.children;
                    kids[candidate.EmptyIndex].motion = restore;
                    candidate.Tree.children = kids;
                    filled++;
                    names.Add(label);
                }
            }

            if (filled == 0)
            {
                if (candidateCount > 0)
                {
                    ctx.Report.Warning(Category,
                        $"{candidateCount} blend-tree toggle(s) were left without a restore animation",
                        "Each is a toggle VRCFury turned into a blend tree whose \"off\" half animates " +
                        "nothing, so its off direction depends on Write Defaults putting the property " +
                        "back. If any of these switch on and never off again, that is why. Nothing was " +
                        "filled because every property they animate is claimed by something else — " +
                        "another toggle in the same tree, or a lower layer that restores it instead.");
                }
                return;
            }
            EditorUtility.SetDirty(master);
            AssetDatabase.SaveAssets();
            ctx.Report.Converted(Category,
                $"{filled} blend-tree toggle(s) given a restore animation for their \"off\" half",
                $"{string.Join(", ", names.Take(6))}{(names.Count > 6 ? ", …" : "")}" +
                (reused > 0
                    ? $" — {reused} of them reuse an animation the avatar ALREADY had; the rest were generated. "
                    : " — ") +
                "VRCFury rewrites toggle layers into blend trees, and the \"off\" half of each one is " +
                "an empty slot that asserts nothing — the same idiom as VRChat's empty off STATE, one " +
                "level down where the off-state repair could not see it. A toggle built that way can " +
                "switch on and never off again. Each off half now plays a clip holding the value the " +
                "property has on this avatar right now, so it restores by animating. If a toggle " +
                "should rest in its OTHER position, set that up on the avatar before converting: " +
                "whatever is true at conversion time is what \"off\" now means." +
                (contested.Count > 0
                    ? $"\n\nToggles with at least one property left to nobody ({contested.Count}): " +
                      $"{string.Join(", ", contested.Take(6))}{(contested.Count > 6 ? ", …" : "")}. " +
                      "Something else in the same layer animates those properties too — an \"all " +
                      "clothing off\" preset overlapping the individual garments is the usual case. " +
                      "Unlike separate layers, toggles blended into one tree ADD UP instead of the " +
                      "top one winning, so restoring there would fight the preset rather than defer " +
                      "to it: the garment switched ON would read as on again. Those properties keep " +
                      "the behaviour they had in VRChat, and a toggle listed here may still have had " +
                      "its other properties restored."
                    : ""));
        }

        internal static void FillEmptyTreeSlotsWithRestoreClipsForTest(AnimatorController master,
            BridgeContext ctx) => FillEmptyTreeSlotsWithRestoreClips(master, ctx, new HashSet<string>());

        /// <summary>
        /// Every toggle-shaped 1D tree reachable from a motion. A tree that qualifies is NOT
        /// descended into: it is taken as the toggle, and whatever its ON half contains belongs to
        /// that toggle rather than being a toggle of its own.
        /// </summary>
        static void CollectToggleTrees(Motion motion, HashSet<BlendTree> seen, List<ToggleTree> into)
        {
            if (!(motion is BlendTree tree) || !seen.Add(tree))
            {
                return;
            }
            var children = tree.children;
            if (tree.blendType == BlendTreeType.Simple1D && children.Length == 2)
            {
                int empty = -1, holds = -1;
                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i].motion == null || IsCurveless(children[i].motion, null))
                    {
                        // Both halves empty: there is no ON clip to read a property list off, and
                        // the toggle animates nothing in either direction. Nothing to restore.
                        if (empty >= 0)
                        {
                            empty = -1;
                            break;
                        }
                        empty = i;
                    }
                    else
                    {
                        holds = i;
                    }
                }
                if (empty >= 0 && holds >= 0)
                {
                    var floats = new HashSet<EditorCurveBinding>();
                    var objects = new HashSet<EditorCurveBinding>();
                    CollectBindings(children[holds].motion, floats, objects);
                    if (floats.Count > 0 || objects.Count > 0)
                    {
                        into.Add(new ToggleTree
                        {
                            Tree = tree,
                            EmptyIndex = empty,
                            Floats = floats,
                            Objects = objects,
                        });
                        return;
                    }
                }
            }
            foreach (var child in children)
            {
                CollectToggleTrees(child.motion, seen, into);
            }
        }

        /// <summary>
        /// What a layer animates OUTSIDE the toggles found in it — the trees themselves are stepped
        /// over, so what is left is everything with a claim on a property that no single toggle can
        /// answer for.
        /// </summary>
        static void CollectOutsideToggles(Motion motion, HashSet<BlendTree> toggles,
            HashSet<EditorCurveBinding> into)
        {
            if (motion is BlendTree tree)
            {
                if (toggles.Contains(tree))
                {
                    return;
                }
                foreach (var child in tree.children)
                {
                    CollectOutsideToggles(child.motion, toggles, into);
                }
                return;
            }
            var floats = new HashSet<EditorCurveBinding>();
            var objects = new HashSet<EditorCurveBinding>();
            CollectBindings(motion, floats, objects);
            into.UnionWith(floats);
            into.UnionWith(objects);
        }

        /// <summary>
        /// What to call a toggle tree in the report and on disk. Fury names them after the toggle,
        /// which is what a reader recognises; length is capped because some carry a whole object
        /// path and the file name has a project path in front of it.
        /// </summary>
        static string ToggleTreeLabel(BlendTree tree, string layerName)
        {
            string name = tree != null ? tree.name : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = layerName;
            }
            return name.Length > 60 ? name.Substring(0, 60).TrimEnd() : name;
        }

        /// <summary>
        /// Removes restore clips left in the output folder by an earlier conversion of this same
        /// avatar. Only files this pass names, only in this avatar's own output folder, and only
        /// ones the controller just built does not reference — so what goes is exactly the litter
        /// from a previous run, which reconverting has already replaced.
        /// </summary>
        static int DeleteStaleRestoreClips(string dir, HashSet<string> keep)
        {
            if (!AssetDatabase.IsValidFolder(dir))
            {
                return 0;
            }
            int removed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || keep.Contains(path))
                {
                    continue;
                }
                // " restore.anim" and " restore 4.anim" — the shapes this pass has ever written.
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                int cut = file.LastIndexOf(" restore", StringComparison.Ordinal);
                if (cut < 0)
                {
                    continue;
                }
                string tail = file.Substring(cut + " restore".Length).Trim();
                if (tail.Length > 0 && !int.TryParse(tail, out _))
                {
                    continue;
                }
                if (AssetDatabase.DeleteAsset(path))
                {
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// Whether an empty state is a ROUTER — somewhere the layer passes through — rather than an
        /// "off" state it comes to rest in. Only the second kind may be given a restore clip.
        ///
        /// VRChat avatars are full of routers. The common one is a local/remote gate: an empty
        /// default state named something like "LocalCheck" whose outgoing transitions split on
        /// <c>IsLocal</c>, one branch driven by the wearer's own controls and the other by a synced
        /// dropdown. It is empty ON PURPOSE — it exists to choose, not to assert. Handing it the
        /// values of whatever it happens to lead to makes it hold those values for as long as the
        /// layer sits there, and on any avatar whose gate condition never resolves, that is forever.
        /// A hat grab layer was found doing exactly this.
        ///
        /// Two shapes say "you cannot stay here", and neither can occur in the toggle idiom:
        ///
        ///   - an outgoing transition with NO conditions, which always fires;
        ///   - the same parameter compared Greater in one transition and Less in another, which
        ///     between them cover every value it can hold.
        ///
        /// The second is deliberately measured ACROSS transitions, not within one. A single
        /// transition carrying both — <c>GestureLeft &gt; 3.9 &amp;&amp; &lt; 4.1</c> — is a band
        /// asking for one specific value, which is a perfectly ordinary way for a toggle's off
        /// state to wait for a gesture. Counting that as a router would skip the very layers this
        /// pass exists for.
        /// </summary>
        static bool IsPassThroughState(AnimatorState state)
        {
            var transitions = state != null ? state.transitions : null;
            if (transitions == null || transitions.Length == 0)
            {
                return false; // nowhere to go: this is where the layer lives
            }
            var openedAbove = new HashSet<string>();
            var openedBelow = new HashSet<string>();
            foreach (var transition in transitions)
            {
                if (transition == null)
                {
                    continue;
                }
                var conditions = transition.conditions;
                if (conditions == null || conditions.Length == 0)
                {
                    return true; // unconditional exit
                }
                var above = new HashSet<string>();
                var below = new HashSet<string>();
                foreach (var condition in conditions)
                {
                    if (condition.mode == AnimatorConditionMode.Greater)
                    {
                        above.Add(condition.parameter);
                    }
                    else if (condition.mode == AnimatorConditionMode.Less)
                    {
                        below.Add(condition.parameter);
                    }
                }
                // A band within one transition asks for a value; it doesn't cover the space.
                var band = new HashSet<string>(above);
                band.IntersectWith(below);
                above.ExceptWith(band);
                below.ExceptWith(band);
                openedAbove.UnionWith(above);
                openedBelow.UnionWith(below);
            }
            openedAbove.IntersectWith(openedBelow);
            return openedAbove.Count > 0;
        }

        /// <summary>
        /// An existing clip that does exactly what the generated one would: the same bindings,
        /// held at the same values. Requires an EXACT match on both — a clip that restores most
        /// of what is needed would leave the rest changed, which is the bug being fixed.
        /// </summary>
        static AnimationClip FindEquivalentClip(AnimationClip generated, HashSet<AnimationClip> candidates)
        {
            var wantFloats = AnimationUtility.GetCurveBindings(generated);
            var wantObjects = AnimationUtility.GetObjectReferenceCurveBindings(generated);

            foreach (var candidate in candidates)
            {
                if (candidate == null)
                {
                    continue;
                }
                var haveFloats = AnimationUtility.GetCurveBindings(candidate);
                var haveObjects = AnimationUtility.GetObjectReferenceCurveBindings(candidate);
                if (haveFloats.Length != wantFloats.Length || haveObjects.Length != wantObjects.Length)
                {
                    continue;
                }

                bool same = true;
                foreach (var binding in wantFloats)
                {
                    var mine = AnimationUtility.GetEditorCurve(generated, binding);
                    var theirs = AnimationUtility.GetEditorCurve(candidate, binding);
                    // A restore clip holds one value; anything that moves over time is a
                    // different animation, whatever it happens to start at.
                    if (theirs == null || theirs.length == 0 || mine == null || mine.length == 0
                        || !Mathf.Approximately(theirs.keys[0].value, mine.keys[0].value)
                        || !Mathf.Approximately(theirs.keys[theirs.length - 1].value, mine.keys[0].value))
                    {
                        same = false;
                        break;
                    }
                }
                if (!same)
                {
                    continue;
                }
                foreach (var binding in wantObjects)
                {
                    var mine = AnimationUtility.GetObjectReferenceCurve(generated, binding);
                    var theirs = AnimationUtility.GetObjectReferenceCurve(candidate, binding);
                    if (theirs == null || theirs.Length == 0 || mine == null || mine.Length == 0
                        || theirs[0].value != mine[0].value)
                    {
                        same = false;
                        break;
                    }
                }
                if (same)
                {
                    return candidate;
                }
            }
            return null;
        }

        static void CollectBindings(Motion motion, HashSet<EditorCurveBinding> floats,
            HashSet<EditorCurveBinding> objects)
        {
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                {
                    CollectBindings(child.motion, floats, objects);
                }
                return;
            }
            if (!(motion is AnimationClip clip))
            {
                return;
            }
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                floats.Add(binding);
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                objects.Add(binding);
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
                        bool baseLayer = layer.name.StartsWith("[Base]", StringComparison.Ordinal);
                        ctx.Report.Warning(Category, $"Layer \"{layer.name}\" animates body muscles or root motion",
                            baseLayer
                                ? "It sits ABOVE ChilloutVR's own Locomotion/Emotes layer and drives the same " +
                                  "muscles, so it does not add to CVR's locomotion — it REPLACES it. That is " +
                                  "what \"Base / locomotion\" means, and it is the right choice only if this " +
                                  "avatar's own locomotion system runs correctly here. If it does not, the " +
                                  "symptoms are unmistakable: the movement sliders animate nothing, and the " +
                                  "Airborne / Flying / Sitting / Swimming stances do nothing, because the layer " +
                                  "that answers them has been overridden. VRChat locomotion replacements often " +
                                  "depend on runtime layer-weight control and on parameters ChilloutVR feeds " +
                                  "differently, neither of which converts. THE FIX IS ONE CLICK: turn OFF " +
                                  "\"Base / locomotion\" in Animator layers to convert and convert again — " +
                                  "ChilloutVR's own locomotion is complete and needs nothing from VRChat."
                                : "It can override CVR's locomotion/pose. Review it; lower its weight or delete " +
                                  "it if movement breaks.");
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

            // PhysBone on/off curves with nowhere to land: the chain they name produced no
            // physics, so there is no component to retarget them at. The curve then points at a
            // VRCPhysBone that gets deleted with the rest of the VRC components, and the toggle
            // that drives it does nothing at all — silently, because every OTHER part of it
            // converts perfectly. The menu entry appears, the parameter syncs, the layer plays.
            //
            // Found via a tester whose ear, butt and tail scaling toggles "did nothing": all
            // three chains had been skipped earlier for constraint conflicts, each with its own
            // report entry saying so — but nothing connected those skips to the toggles that
            // depended on them, so the two facts sat in the same report and never met.
            var strandedToggles = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

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

                    // Recorded before the search, cleared by a match below. A PhysBone curve that
                    // finds no chain is the stranded case; object toggles are not, because an
                    // object toggle still does its own job whether or not physics rode along.
                    bool physBoneRetargeted = false;

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
                            physBoneRetargeted = true;
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

                    // Nothing to retarget at, so this curve dies with the VRC components. Both
                    // facts are needed to make it actionable: which clip, and which PhysBone
                    // object — the physics section's skip entry for that same path says WHY it
                    // was not converted.
                    if (physBoneToggle && !physBoneRetargeted)
                    {
                        if (!strandedToggles.TryGetValue(binding.path, out var clipNames))
                        {
                            strandedToggles[binding.path] = clipNames = new SortedSet<string>(StringComparer.Ordinal);
                        }
                        clipNames.Add(clip.name);
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

            if (strandedToggles.Count > 0)
            {
                var lines = strandedToggles
                    .Select(entry => $"\"{entry.Key}\" (in {string.Join(", ", entry.Value)})")
                    .ToList();
                ctx.Report.Warning(Category,
                    $"{strandedToggles.Count} animation(s) switch a PhysBone that wasn't converted — " +
                    "those controls will do nothing",
                    string.Join("; ", lines) + " — these clips turn a VRChat PhysBone on or off, " +
                    "which is how avatars pause a chain while a body part is resized. The chain " +
                    "they name produced no physics here, so there is no cloth component to switch " +
                    "instead, and the curve dies with the VRC components. Everything else about " +
                    "the control converts — menu entry, parameter, animator layer — so it looks " +
                    "correct and does nothing, which is the worst way for this to present. " +
                    "The PhysBones -> MagicaCloth2 section above has a Skipped entry for each of " +
                    "these paths saying WHY it wasn't converted (a constraint driving a bone in " +
                    "the chain is the usual reason); fix that and the toggle starts working. If " +
                    "the chain was never meant to be simulated, remove the control instead.");
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

            // The SOURCE hierarchy, so a dead path can be blamed correctly.
            //
            // "44 clips animate paths that don't exist" is the loudest thing this report says, and
            // on a healthy avatar it is usually not our doing — a quadruped base fired it 44 times
            // for clips addressing a configuration that prefab simply wasn't in, all of them
            // equally inert in VRChat. Shouting about those buries the cases that matter and makes
            // a clean conversion look broken. So each dead path is checked against the avatar as it
            // arrived: still missing there, and it was already silent before AvatarBridge touched
            // it; present there but not here, and something in this conversion moved or stripped
            // it, which is a real defect and is reported as one.
            var sourceRoot = ctx.SourceDescriptor != null ? ctx.SourceDescriptor.transform : null;
            var sourceCache = new Dictionary<string, bool>();
            bool ResolvedBefore(string path)
            {
                if (sourceRoot == null || string.IsNullOrEmpty(path))
                {
                    return false; // no source to compare against: never claim we broke it
                }
                if (!sourceCache.TryGetValue(path, out var was))
                {
                    sourceCache[path] = was = sourceRoot.Find(path) != null;
                }
                return was;
            }

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
            // Clips whose dead paths DID resolve before conversion — the ones we are responsible for.
            var lostClips = new List<(string clip, int dead, int total, string example)>();

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
                int dead = 0, total = 0, lost = 0;
                string example = null;
                string lostExample = null;
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
                        if (ResolvedBefore(binding.path))
                        {
                            lost++;
                            lostExample = lostExample ?? binding.path;
                        }
                    }
                }
                if (lost > 0)
                {
                    lostClips.Add((clip.name, lost, total, lostExample));
                }
                else if (dead > 0)
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

            // Paths this conversion lost. Rare, and always worth acting on.
            if (lostClips.Count > 0)
            {
                lostClips.Sort((a, b) => b.dead.CompareTo(a.dead));
                var lostLines = lostClips.Take(8)
                    .Select(b => $"\"{b.clip}\" ({b.dead} of {b.total}, e.g. \"{b.example}\")");
                ctx.Report.Warning(Category,
                    $"{lostClips.Count} clip(s) LOST paths that existed before conversion",
                    string.Join("; ", lostLines) + (lostClips.Count > 8 ? "; …" : "") + ". These " +
                    "objects were on the avatar when it arrived and are not on it now, so these " +
                    "curves worked in VRChat and play as silence here. Something in this conversion " +
                    "moved or removed them — a stripped system (GoGo, SPS) taking objects a clip " +
                    "still references is the usual innocent explanation, and turning that strip off " +
                    "and converting again will tell you. Anything else is a bug worth reporting.");
            }

            if (broken.Count == 0)
            {
                return;
            }
            broken.Sort((a, b) => b.dead.CompareTo(a.dead));
            var lines = broken.Take(8)
                .Select(b => $"\"{b.clip}\" ({b.dead} of {b.total}, e.g. \"{b.example}\")");
            // NOT a warning. These paths were already missing on the source avatar, so the curves
            // were silent in VRChat too and nothing was lost in conversion. Flagging them as
            // problems made healthy conversions look broken — one quadruped base tripped this 44
            // times for clips addressing a configuration that prefab wasn't set up for.
            ctx.Report.Converted(Category,
                $"{broken.Count} clip(s) animate paths that were ALREADY missing in VRChat",
                string.Join("; ", lines) + (broken.Count > 8 ? "; …" : "") + ". Checked against the " +
                "avatar as it arrived: these objects weren't there either, so Unity played the " +
                "curves as silence in VRChat exactly as it will here. Nothing was lost in " +
                "conversion and there is usually nothing to do. Two cases are worth a look: a " +
                "build-time tool (VRCFury path rewriting, Modular Avatar) may have been fixing the " +
                "paths at upload — if so, install that package here and convert again so its bake " +
                "runs first — or the clip belongs to a feature this avatar variant isn't configured " +
                "for, which is normal and harmless.");
        }

        /// <summary>
        /// Material animations that write to a property their own shader does not have.
        ///
        /// This is the quietest failure on the platform. The toggle appears in the menu, the
        /// parameter syncs, the layer plays its clip at weight 1 — the CCK Debugger and this
        /// tool's own layer readout both show it working — and nothing happens on screen,
        /// because the value is being written to a uniform that does not exist.
        ///
        /// The usual cause is a LOCKED (optimised) Poiyomi/Thry shader. Locking inlines every
        /// property that was not flagged animated AT LOCK TIME as a literal constant and deletes
        /// it from the shader. Flagging a property afterwards sets `_<Name>Animated` on the
        /// material but changes nothing until the material is unlocked and locked again — so a
        /// material can claim a property is animated while its shader has no such property. On
        /// the avatar that prompted this, a "wet skin" toggle wrote _DetailNormalMapScale and
        /// _Matcap3Intensity to a shader whose entire Properties block was 46 lines and
        /// contained neither.
        ///
        /// Nothing here can be fixed by conversion — the same animation is equally dead in
        /// VRChat — but saying so precisely is the difference between a five-minute re-lock and
        /// a day spent looking at the animator, which is where every other clue points.
        /// </summary>
        static void AuditMaterialProperties(AnimatorController master, BridgeContext ctx)
        {
            var root = ctx.Target.transform;
            var seen = new HashSet<AnimationClip>();
            // property -> (how many bindings, an example path, the shader to blame)
            var dead = new Dictionary<string, (int count, string path, string shader)>();
            var locked = new HashSet<string>();
            // Properties whose material ALREADY carries Poiyomi's animated flag and STILL has no
            // such property in its shader. That combination is the difference between "nobody
            // flagged it" and "flagging it did not help", and the second is not fixable by
            // repeating the flag-and-relock advice.
            var flaggedAndStillMissing = new HashSet<string>();

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
                    if (!binding.propertyName.StartsWith("material."))
                    {
                        continue;
                    }
                    string property = binding.propertyName.Substring("material.".Length);
                    // Colour and vector channels arrive one component at a time
                    // ("material._Color.r"); the property is everything before the channel.
                    int dot = property.LastIndexOf('.');
                    if (dot > 0 && property.Length - dot == 2)
                    {
                        property = property.Substring(0, dot);
                    }
                    var target = string.IsNullOrEmpty(binding.path) ? root : root.Find(binding.path);
                    var renderer = target != null ? target.GetComponent<Renderer>() : null;
                    if (renderer == null)
                    {
                        continue; // a dead path — AuditClipBindings owns that report
                    }
                    Material carrier = null;
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material != null && material.HasProperty(property))
                        {
                            carrier = material;
                            break;
                        }
                    }
                    if (carrier != null)
                    {
                        continue;
                    }
                    string shaderName = null;
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (material != null && material.shader != null)
                        {
                            shaderName = material.shader.name;
                            // Thry's optimiser writes its output under these names; a locked
                            // shader is the difference between "author forgot" and "author
                            // flagged it but never re-locked".
                            if (shaderName.StartsWith("Hidden/Locked/")
                                || material.shader.name.Contains("/OptimizedShaders/"))
                            {
                                locked.Add(property);
                            }
                            // Poiyomi records "animate this" as an override tag. Finding one
                            // here means the author already did the thing the usual advice
                            // tells them to do, and the property STILL is not in the shader —
                            // so the advice is wrong for this property and saying it again
                            // would send them round the same loop.
                            if (!string.IsNullOrEmpty(material.GetTag(property + "Animated", false, "")))
                            {
                                flaggedAndStillMissing.Add(property);
                            }
                            break;
                        }
                    }
                    dead.TryGetValue(property, out var entry);
                    dead[property] = (entry.count + 1, entry.path ?? binding.path, entry.shader ?? shaderName);
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        Audit(child.state != null ? child.state.motion : null);
                    }
                });
            }

            if (dead.Count == 0)
            {
                return;
            }
            var fixable = dead.Keys.Where(p => !flaggedAndStillMissing.Contains(p)).ToList();
            var stubborn = dead.Keys.Where(flaggedAndStillMissing.Contains).ToList();
            string List(IEnumerable<string> names) =>
                string.Join(", ", names.Take(6)) + (names.Count() > 6 ? ", …" : "");

            var worst = dead.OrderByDescending(p => p.Value.count).ThenBy(p => p.Key, StringComparer.Ordinal).Take(6)
                .Select(p => $"{p.Key} ({p.Value.count} renderer(s), e.g. \"{p.Value.path}\")");
            bool anyLocked = locked.Count > 0;
            ctx.Report.Warning(Category,
                $"{dead.Count} animated material property(ies) don't exist on the shader they target",
                string.Join("; ", worst) + (dead.Count > 6 ? "; …" : "") + ". " +
                (anyLocked
                    ? "These materials use a LOCKED (optimised) Poiyomi/Thry shader, which bakes any " +
                      "property that wasn't flagged animated into a fixed value and deletes it, so " +
                      "writing to it does nothing.\n\n" +
                      (fixable.Count > 0
                          ? $"WORTH FIXING ({fixable.Count}): {List(fixable)} — nothing has flagged these " +
                            "yet. In Poiyomi's material inspector: unlock, right-click the property, mark " +
                            "it animated, lock again. Marking it there also switches on the shader " +
                            "SECTION it belongs to, which is why it has to be done in Poiyomi's UI and " +
                            "not by editing the material file.\n\n"
                          : "") +
                      (stubborn.Count > 0
                          ? $"PROBABLY NOT FIXABLE ({stubborn.Count}): {List(stubborn)} — these are " +
                            "ALREADY flagged animated on the material, and the property still isn't in " +
                            "the shader. Someone has done the unlock-flag-relock already and it did not " +
                            "take. That happens when the property's shader section is switched off (a " +
                            "disabled section is compiled out entirely, and no flag brings it back), or " +
                            "when the animation was authored against a different Poiyomi version than " +
                            "the one installed. Re-locking again will not change it. Enabling the right " +
                            "section on the material might, if you know which one it is; otherwise treat " +
                            "these as lost with the avatar as it stands.\n\n"
                          : "")
                    : "Whatever drives them will appear to work — parameter synced, layer playing, clip at " +
                      "full weight — and change nothing on screen. Check the property name against the " +
                      "shader, or assign the material the animation was authored for. ") +
                "This is not caused by conversion: the same animation is equally dead in VRChat, so a " +
                "toggle that visibly worked there points at a build-time step (Poiyomi's auto-lock on " +
                "upload) that this project isn't running.");
        }

        /// <summary>
        /// Says what happened to the avatar's OWN face tracking when the user chose to keep it.
        ///
        /// The report used to answer that question with "Face tracking not set up (chosen) —
        /// mode is None", which is true of the setting and badly misleading about the outcome:
        /// the window calls that choice "Keep the avatar's own rig", and a reader who has just
        /// watched their VRCFT parameters not appear anywhere reads "None" as "it was dropped".
        /// Counting what actually survived the merge answers it directly.
        /// </summary>
        static void ReportKeptFaceTracking(AnimatorController master, BridgeContext ctx)
        {
            if (ctx.Settings.faceTrackingMode != FaceTrackingMode.None)
            {
                return;
            }
            var kept = master.parameters
                .Select(p => p.name)
                .Where(AvatarFeatureDetect.IsFaceTrackingParameter)
                .ToList();
            if (kept.Count == 0)
            {
                return;
            }
            var shown = kept.Take(6).Select(AvatarFeatureDetect.FaceTrackingShortName);
            ctx.Report.Converted(Category,
                $"Kept the avatar's own face tracking rig — {kept.Count} parameter(s) came through",
                $"{string.Join(", ", shown)}{(kept.Count > 6 ? ", …" : "")}. Its layers, clips and " +
                "parameters were merged like any others and nothing was replaced, because face " +
                "tracking is set to \"Keep the avatar's own rig\". Drive these in the CCK Animator " +
                "Tester's Face tracking section to confirm the shapes still move. Whether a headset " +
                "feeds them in game depends on the rig's own OSC setup, which is unchanged by " +
                "conversion and outside what this tool touches.");
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
        /// Removes animation that provably cannot do anything, and everything that existed only
        /// to drive it.
        ///
        /// A locked Poiyomi shader deletes the properties it baked, so a clip writing to one is
        /// writing into nowhere — in ChilloutVR and equally in VRChat, which is where these
        /// avatars normally arrive from already broken. AuditMaterialProperties names them;
        /// leaving them in place means shipping a menu full of sliders that do nothing, and the
        /// next person to test the avatar spends their evening on the animator.
        ///
        /// Only the individually dead CURVES go. A clip animating a property across thirty
        /// renderers where nine still have it keeps those nine. A clip left with no curves at
        /// all, in a layer where every state is likewise empty, means the layer cannot do
        /// anything either — and once the layer goes, the parameter is unread and the existing
        /// menu pruning takes the control with it. That cascade is the point: it is what turns
        /// "the slider does nothing" into "there is no slider".
        ///
        /// Runs after AnimationSelfContainer, so every clip touched is the conversion's own copy
        /// in the output folder. The source avatar's clips are never modified.
        /// </summary>
        internal static void StripDeadMaterialCurves(BridgeContext ctx)
        {
            var master = ctx.MergedController;
            if (master == null || !ctx.Settings.stripDeadMaterialAnimation)
            {
                return;
            }
            var root = ctx.Target.transform;

            // Renderers whose material SLOTS are animated are off limits. A clip that swaps in a
            // different material makes "the current material has no such property" a statement
            // about this instant, not about the avatar — the swapped-in material may well have
            // it, and stripping would break a working effect.
            var swapped = new HashSet<Renderer>();
            var clips = new HashSet<AnimationClip>();
            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        CollectClips(child.state != null ? child.state.motion : null, clips);
                    }
                });
            }
            foreach (var clip in clips)
            {
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (!binding.propertyName.StartsWith("m_Materials."))
                    {
                        continue;
                    }
                    var target = string.IsNullOrEmpty(binding.path) ? root : root.Find(binding.path);
                    var renderer = target != null ? target.GetComponent<Renderer>() : null;
                    if (renderer != null)
                    {
                        swapped.Add(renderer);
                    }
                }
            }

            var byProperty = new Dictionary<string, int>();
            int curvesRemoved = 0, clipsTouched = 0, clipsEmptied = 0;

            foreach (var clip in clips)
            {
                var doomed = new List<EditorCurveBinding>();
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!binding.propertyName.StartsWith("material."))
                    {
                        continue;
                    }
                    string property = binding.propertyName.Substring("material.".Length);
                    int dot = property.LastIndexOf('.');
                    if (dot > 0 && property.Length - dot == 2)
                    {
                        property = property.Substring(0, dot);
                    }
                    var target = string.IsNullOrEmpty(binding.path) ? root : root.Find(binding.path);
                    var renderer = target != null ? target.GetComponent<Renderer>() : null;
                    // Unresolvable path: AuditClipBindings owns that, and guessing here could
                    // delete animation for an object a later step restores.
                    if (renderer == null || swapped.Contains(renderer))
                    {
                        continue;
                    }
                    if (renderer.sharedMaterials.Any(m => m != null && m.HasProperty(property)))
                    {
                        continue;
                    }
                    doomed.Add(binding);
                    byProperty.TryGetValue(property, out int seen);
                    byProperty[property] = seen + 1;
                }
                if (doomed.Count == 0)
                {
                    continue;
                }
                foreach (var binding in doomed)
                {
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                }
                curvesRemoved += doomed.Count;
                clipsTouched++;
                if (AnimationUtility.GetCurveBindings(clip).Length == 0
                    && AnimationUtility.GetObjectReferenceCurveBindings(clip).Length == 0)
                {
                    clipsEmptied++;
                }
                EditorUtility.SetDirty(clip);
            }

            if (curvesRemoved == 0)
            {
                return;
            }

            int layersRemoved = RemoveEmptyToggleLayers(master, ctx);
            // The parameter and menu pruning that already exists does the rest of the cascade,
            // now that nothing reads those parameters.
            PruneOrphanedParameters(master, ctx);
            PruneDeadMenuEntries(master, ctx);
            EditorUtility.SetDirty(master);
            AssetDatabase.SaveAssets();

            var worst = byProperty.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(6)
                .Select(p => $"{p.Key} ({p.Value})");
            ctx.Report.Converted(Category,
                $"Removed {curvesRemoved} animation curve(s) that could never have done anything",
                $"{string.Join(", ", worst)}{(byProperty.Count > 6 ? ", …" : "")} — across " +
                $"{clipsTouched} clip(s), of which {clipsEmptied} were left animating nothing" +
                (layersRemoved > 0 ? $", and {layersRemoved} layer(s) removed with them" : "") +
                ". These wrote to material properties the shader on those renderers does not have, " +
                "so they did nothing here and did nothing in VRChat either. Removing them takes the " +
                "dead sliders and toggles out of your menu instead of leaving controls that move and " +
                "change nothing.\n\nGETTING THEM BACK IS MANUAL, AND MAY NOT BE POSSIBLE. The warning " +
                "above splits them: the ones nothing has flagged yet usually come back after an " +
                "unlock, flag and re-lock in Poiyomi's material inspector, and converting again picks " +
                "them up. The ones already flagged and still missing have had that done to them " +
                "once and it did not take — their shader section is switched off, or the animation " +
                "predates the installed Poiyomi — and no amount of re-locking will change it. If you " +
                "would rather ship the controls anyway, dead or not, turn off \"Remove animation that " +
                "can't do anything\" in Advanced.\n\nNothing on the source avatar was changed; only " +
                "the conversion's own copies of the clips were edited.");
        }

        /// <summary>
        /// Layers whose every state now animates nothing. Deliberately narrow: a layer with any
        /// state behaviour is left alone (a driver still fires), as are the CCK's own layers and
        /// anything injected, because "does nothing visible" is not the same as "does nothing".
        /// </summary>
        static int RemoveEmptyToggleLayers(AnimatorController master, BridgeContext ctx)
        {
            var keep = new List<AnimatorControllerLayer>();
            var dropped = new List<string>();
            foreach (var layer in master.layers)
            {
                if (IsProtectedLayer(layer.name) || !LayerAnimatesNothing(layer))
                {
                    keep.Add(layer);
                    continue;
                }
                dropped.Add(layer.name);
            }
            if (dropped.Count == 0)
            {
                return 0;
            }
            master.layers = keep.ToArray();
            ctx.Report.Converted(Category,
                $"Removed {dropped.Count} layer(s) left animating nothing",
                string.Join(", ", dropped.Take(8)) + (dropped.Count > 8 ? ", …" : "") +
                " — every clip in them wrote only to material properties their shader doesn't have.");
            return dropped.Count;
        }

        static bool IsProtectedLayer(string name)
        {
            return name == "Locomotion/Emotes" || name == "LeftHand" || name == "RightHand"
                   || name == "Size" || name == "Linear Smoothing Layer"
                   || name.StartsWith("[FT] ");
        }

        static bool LayerAnimatesNothing(AnimatorControllerLayer layer)
        {
            bool empty = true;
            WalkMachines(layer.stateMachine, machine =>
            {
                if (machine.behaviours != null && machine.behaviours.Length > 0)
                {
                    empty = false;
                }
                foreach (var child in machine.states)
                {
                    if (child.state == null)
                    {
                        continue;
                    }
                    if (child.state.behaviours != null && child.state.behaviours.Length > 0)
                    {
                        empty = false;
                        return;
                    }
                    if (MotionAnimatesSomething(child.state.motion))
                    {
                        empty = false;
                        return;
                    }
                }
            });
            return empty;
        }

        static bool MotionAnimatesSomething(Motion motion)
        {
            if (motion is BlendTree tree)
            {
                return tree.children.Any(c => MotionAnimatesSomething(c.motion));
            }
            if (!(motion is AnimationClip clip))
            {
                return false;
            }
            return AnimationUtility.GetCurveBindings(clip).Length > 0
                   || AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0;
        }

        static void CollectClips(Motion motion, HashSet<AnimationClip> into)
        {
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                {
                    CollectClips(child.motion, into);
                }
            }
            else if (motion is AnimationClip clip)
            {
                into.Add(clip);
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
                    $"The saved controller references {intoTemp} bake-temp (VRCFury/NDMF) and {introduced} unresolvable asset(s)",
                    $"e.g. {badSample}. VRCFury deletes its temp folder on its next build — which entering " +
                    "play mode triggers if the original avatar is still in the scene — and an " +
                    "unresolvable reference is already dead. Either way those animations will stop " +
                    "working after the next play or Fury build. Do not upload this conversion." +
                    (introduced > 0
                        // Blaming the conversion outright sent someone hunting a bug on this side
                        // when the real cause was a VRCFury bake failing partway: the controller
                        // referenced assets Fury never finished writing, and the same avatar showed
                        // hundreds of Fury exceptions in the editor log. Check that first, because a
                        // half-built bake produces exactly this and nothing here can repair it.
                        ? " CHECK YOUR BAKE FIRST: if VRCFury or Modular Avatar errored while baking " +
                          "this avatar, the assets it was still writing never arrived and the references " +
                          "point at nothing. Build a test copy of the SOURCE avatar on its own (Tools > " +
                          "VRCFury > Build a Test Copy) and see whether it completes cleanly — a version " +
                          "mismatch between the avatar's package and your installed VRCFury is the usual " +
                          "cause. If that bake is clean and this still happens, it is a conversion bug: " +
                          "please report it with this file attached."
                        : ""));
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

        static AvatarMask _handLeftMask, _handRightMask, _handsOnlyMask, _musclesOnlyMask, _noMuscleMask, _fingersOnlyMask, _noFingersMask;

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

        /// <summary>
        /// Everything EXCEPT fingers, for a layer that would otherwise overwrite a hand pose.
        ///
        /// Needed for layers that arrive with no mask at all. The hand-pose audit could only ever
        /// edit an existing mask, so an unmasked layer sailed through it — and unmasked is exactly
        /// what a merged FX layer full of finger curves ends up as, because MaskMergedLayers reads
        /// muscle curves as deliberate body animation and leaves it alone.
        /// </summary>
        static AvatarMask GetNoFingersMask(BridgeContext ctx)
        {
            if (_noFingersMask != null)
            {
                return _noFingersMask;
            }
            var parts = new List<AvatarMaskBodyPart>();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                if (part != AvatarMaskBodyPart.LeftFingers && part != AvatarMaskBodyPart.RightFingers)
                {
                    parts.Add(part);
                }
            }
            return _noFingersMask = BuildRigMask("AvatarBridge_NoFingers", ctx, parts.ToArray());
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
            _noMuscleMask = _fingersOnlyMask = _noFingersMask = null;
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
