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
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    // Merges the selected VRChat playable layers into one ChilloutVR
    // controller on the CCK's AvatarAnimator. Rewrites the VRC-specific
    // parts: gestures, parameter names, "#" locals, drivers, masks.
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
        // GestureLeftWeight/RightWeight stay unrenamed.
        // A CVRParameterStream feeds them instead.
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

        // Fed from the game by a CVRParameterStream.
        // Never "#" prefixed. The stream runs on the wearer's copy only,
        // so sync is the sole path to other clients.
        static readonly HashSet<string> StreamFedParameters = new HashSet<string>
        {
            "GestureLeftWeight", "GestureRightWeight", "MuteSelf", "VRMode",
            "Upright", "TrackingType", "EyeHeightAsMeters"
            // The rest of the scale family is derived locally from
            // EyeHeightAsMeters by FeedScaleParameters. Cheaper than sync.
        };

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

        // CVR drives these non-zero at runtime.
        // Matching defaults avoids startup glitches.
        static readonly Dictionary<string, float> NonZeroDefaults = new Dictionary<string, float>
        {
            { "Grounded", 1f }
        };

        // VRC built-ins with no CVR equivalent. Frozen "#" locals.
        // Stream-fed names are live instead; see StreamFedParameters.
        // AFK is absent on purpose: it syncs natively in CVR.
        // VelocityMagnitude is here for the "#" prefix only.
        // FeedVelocityMagnitude recomputes it every frame.
        // The scale family is stream-fed or derived; see FeedScaleParameters.
        static readonly HashSet<string> KnownUnsupportedVrcParameters = new HashSet<string>
        {
            "Earmuffs", "AngularY",
            "AvatarVersion", "VelocityMagnitude", "GroundProximity", "InStation",
            "IsAnimatorEnabled"
        };

        public static void Run(BridgeContext ctx)
        {
            var vrcControllers = GetSelectedVrcControllers(ctx);
            ReportSkippedLayers(ctx);
            bool convertingGestureLayer = vrcControllers.Any(c => c.id == VRCAvatarDescriptor.AnimLayerType.Gesture);

            // Captured before merging. The saved-controller audit uses
            // these to tell inherited dead references from introduced ones.
            _sourceControllerGuids.Clear();
            foreach (var (_, sourceController) in vrcControllers)
            {
                CollectSerializedGuids(AssetDatabase.GetAssetPath(sourceController), _sourceControllerGuids);
            }

            // The Action transplant and LocomotionGrafter share
            // per-conversion clone caches. Reset before the merge loop.
            LocomotionGrafter.ResetClones();
            DroppedPlayAudio.Clear();
            DroppedPlayAudioCount = 0;
            ConvertedPlayAudio.Clear();
            ApproximatedPlayAudio.Clear();
            AudioWindowClones.Clear();
            DroppedPoseSpace.Clear();
            DroppedPoseSpaceCount = 0;
            MergedLayerNames.Clear();
            CapturedLayerControls.Clear();

            AnimatorController master = LoadBaseController(ctx, convertingGestureLayer);
            var masterLayers = master.layers.ToList();
            var vrcLayers = new List<AnimatorControllerLayer>();

            // In keep-GoGo mode Base/Additive/Action replace CVR's
            // locomotion and must run at full weight.
            bool gogoDrivesLocomotion = !ctx.Settings.stripGogoLoco && SystemStripper.AvatarUsesGogo(ctx);
            int actionLayersRested = 0;
            // Action layers the avatar drives itself; see the weight decision below.
            var actionFeatures = new List<string>();
            var actionMoved = new List<string>();

            foreach (var (id, controller) in vrcControllers)
            {
                // VRChat holds the Action playable at weight 0 and raises
                // it only during emotes. Merged here it would run forever.
                // Weight 0 is the faithful conversion. CVR's own
                // Locomotion/Emotes layer still plays emotes.
                bool actionAtRest = id == VRCAvatarDescriptor.AnimLayerType.Action && !gogoDrivesLocomotion;
                var copier = new AnimatorDeepCopier();
                MergeParameters(master, controller, ctx);

                bool firstLayerOfController = true;
                int srcIndex = -1;
                foreach (var srcLayer in controller.layers)
                {
                    srcIndex++;
                    if (srcLayer.syncedLayerIndex >= 0)
                    {
                        ctx.Report.Skipped(Category, $"{id} layer \"{srcLayer.name}\"",
                            "Synced layers cannot survive merging into one controller.");
                        continue;
                    }

                    var clone = copier.CloneLayer(srcLayer);
                    // Hand-pose layers take over the freed LeftHand/RightHand slots.
                    string cvrHandName = GetCvrHandLayerName(id, srcLayer);
                    clone.name = MakeUniqueLayerName(masterLayers,
                        cvrHandName ?? $"[{id}] {clone.name}");
                    if (cvrHandName != null)
                    {
                        // Reported so finger-pose issues stay diagnosable.
                        ctx.Report.Converted(Category,
                            $"Gesture hand layer \"{srcLayer.name}\" -> \"{clone.name}\"",
                            "Takes over ChilloutVR's hand-pose slot: the CCK's own layer was " +
                            "dropped and this one — the avatar's actual finger animations — " +
                            "drives the fingers. Its VRChat hand mask is replaced with an " +
                            "equivalent generated copy (same humanoid bits, verified against " +
                            "VRChat's own vrc_Hand masks).");
                    }
                    MergedLayerNames[$"{id}:{srcIndex}"] = clone.name;
                    if (firstLayerOfController)
                    {
                        // Unity forces a controller's first layer to weight 1; once merged it
                        // is no longer first, so bake that weight in.
                        clone.defaultWeight = 1f;
                        firstLayerOfController = false;
                    }
                    if (actionAtRest)
                    {
                        // After the first-layer rule, which would re-raise it.
                        // Always weight 0. Unity gives a layer no way to
                        // yield, so a live Action layer freezes the body.
                        // The pose part is a platform wall; it is reported.
                        clone.defaultWeight = 0f;
                        actionLayersRested++;
                        if (ActionLayerDrivesOwnFeature(clone, out string byWhat))
                        {
                            // Poses move into CVR's locomotion layer.
                            // The clone stays at weight 0 so its drivers still fire.
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
            // The CCK's kept hand layers already use gesture floats. Left alone.
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
            // After the stripper, before any renames. Reads the source
            // controllers, which still carry VRChat parameter names.
            LocomotionGrafter.Run(ctx, master);
            StripExistingFaceTracking(master, vrcLayers, ctx);
            ReplaceAnimatorBlink(master, ctx);
            ToggleNativizer.Run(ctx, master, vrcLayers);
            // Before RenamePass, so menu machineNames still match.
            // Before CompactIntDropdowns, which needs the Ints in place.
            ParameterTypeInference.Run(master, ctx);
            RenamePass(master, vrcLayers, ctx);
            ApplyParameterDefaults(master, ctx);
            ReconcileAasInputTypes(master, ctx);
            // Before CreateParameterStreams, which must see the
            // EyeHeightAsMeters declaration this can add.
            FeedScaleParameters(master, ctx);
            CreateParameterStreams(master, ctx);
            FeedVelocityMagnitude(master, ctx);
            RehomeVolatileAssets(master, vrcLayers, ctx);
            DeduplicateLayers(master, ctx);
            DropStateMachinelessLayers(master, ctx);
            MaskMergedLayers(master, vrcLayers, ctx);
            // Before the empty-state filler. Their preconditions are
            // mutually exclusive, so this order cannot reprocess its output.
            // Before the restore passes, so hoisted layers join ownership.
            HoistContestedTreeToggles(master, ctx);
            // Shared registry of every restore clip this conversion writes.
            // The state filler's sweep spares only clips it knows about.
            // Also stops the passes overwriting each other's clip names.
            var restoreClipPaths = new HashSet<string>();
            RestorePartialOffStates(master, ctx, restoreClipPaths);
            FillEmptyStatesWithRestoreClips(master, ctx, restoreClipPaths);
            // After the state pass, whose sweep would delete these clips.
            // Before FillEmptyMotionSlots, so off halves are still holes.
            FillEmptyTreeSlotsWithRestoreClips(master, ctx, restoreClipPaths);
            // After both fillers, topping only what is still missing.
            // CVR never restores Write Defaults, so every binding
            // must be asserted by real animation.
            AssertOwnedBindingsEverywhere(master, ctx);
            // After the filler; its added motions are what can strobe.
            SuppressAnyStateSelfRestarts(master, ctx);
            WarnLocomotionOverrides(vrcLayers, ctx);
            FaceTrackingInjector.Inject(master, ctx);
            AvatarScalerInjector.Inject(master, ctx);
            // After every layer exists. The stack checked is the one the game runs.
            AuditHandPoseConflicts(master, ctx);
            // After every pass that can hand out a mask. A masked layer
            // applies only the first object-reference binding, so a
            // masked material swap loses every slot but one.
            UnmaskObjectSwapLayers(master, ctx);
            // Run last. CVR silently drops transitions whose comparison
            // does not match the parameter's final type.
            ReconcileConditionModes(master, ctx);
            SyncDriverParameterTypes(master, ctx);
            RepairUnconditionalDriverStates(master, ctx);
            VerifyMenuParameterNames(master, ctx);
            PruneDeadMenuEntries(master, ctx);
            WithdrawSelfDrivenExposures(master, ctx);
            CompactIntDropdowns(master, ctx);
            // After the menu is final. A parameter kept only for a menu
            // entry pruned above would stay declared and cost sync bits.
            // Before pruning, while every reference is still visible.
            RepairPrefixedReferences(master, ctx);
            DeclareDanglingParameters(master, ctx);
            DefaultUnsupportedBuiltIns(master, ctx);
            PruneOrphanedParameters(master, ctx);
            // After pruning, which would throw these parameters away.
            SafeguardBlendParameters(master, ctx);

            ReviveWeightGatedLayers(master, ctx);

            // Right before saving: animations that toggled a converted
            // PhysBone must reach the generated physics too.
            RewirePhysicsToggles(master, ctx);
            AuditSizeBlendshapes(master, ctx);
            RepairClipPaths(master, ctx);
            AuditClipBindings(master, ctx);
            AuditMaterialProperties(master, ctx);
            ReportKeptFaceTracking(master, ctx);
            AuditCurveControlledGameParameters(master, ctx);

            master.name = SanitizeFileName(ctx.Target.name) + "_CVR";

            // Save can hand back a different persisted object.
            // Everything below must reference that one.
            string controllerPath = $"{ctx.OutputDir}/{master.name}.controller";
            // Counted before and after saving. Unity refuses DontSave
            // objects at save time and the reference dangles silently.
            // Any loss is an Error in the report.
            int motionsBeforeSave = CountMotionReferences(master);
            master = AnimatorAssetSaver.Save(master, controllerPath);
            // After the save. AddObjectToAsset needs a persisted asset.
            FillEmptyMotionSlots(ctx, master);
            // The serialized-guid audit runs later, from BridgeConverter,
            // so it judges the final file.
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

            // CVR runs the avatar through the override controller.
            // It must exist, not be left empty.
            var overrides = new AnimatorOverrideController(master) { name = master.name + "_Overrides" };
            string overridesPath = $"{ctx.OutputDir}/{overrides.name}.overrideController";
            overrides = AnimatorAssetSaver.SaveOverride(overrides, overridesPath);

            ctx.CvrAvatar.avatarSettings.baseController = master;
            // The CCK's Advanced Settings editor reads this slot when it
            // regenerates a controller. Leave it filled.
            ctx.CvrAvatar.avatarSettings.baseOverrideController = overrides;
            ctx.CvrAvatar.overrides = overrides;

            var animator = ctx.TargetAnimator;
            if (animator == null)
            {
                // Reported, never silent. Cloned avatars can inherit an
                // Animator whose controller reference is already dead.
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
                // Assign the override, not the base. CVR assigns
                // CVRAvatar.overrides onto the Animator on load.
                //
                // Skipped entirely when references resolve to nothing.
                // The setter rebinds even on a disabled Animator, and
                // Rebind builds the full playable graph. A dangling
                // motion reference there segfaults Unity.
                // CVR reads CVRAvatar.overrides on load, so skipping
                // the assignment loses nothing.
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
                    // Cleared first, then set. A clone can inherit a dead
                    // controller whose failed native rebind refuses any
                    // new controller until the broken one is let go.
                    animator.runtimeAnimatorController = null;
                    animator.runtimeAnimatorController = overrides;

                    // Record a prefab-instance override, or
                    // SaveAsPrefabAssetAndConnect reverts the assignment
                    // to the prefab's own value.
                    if (PrefabUtility.IsPartOfPrefabInstance(animator))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                    }

                    // Read it back. The assignment can silently not take.
                    // A dangling reference reads as null to script while
                    // the GUID survives into the prefab. If it will not
                    // stick, clear it; an empty slot is honest.
                    if (animator.runtimeAnimatorController != overrides)
                    {
                        // The Inspector's path, not the API's. Dragging
                        // writes serialized m_Controller directly, and that
                        // works where the native setter stores null.
                        // The prefab saves from serialized data; the native
                        // side binds from it on the next load.
                        var serialized = new SerializedObject(animator);
                        serialized.FindProperty("m_Controller").objectReferenceValue = overrides;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        if (PrefabUtility.IsPartOfPrefabInstance(animator))
                        {
                            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                        }

                        // Make the native side notice now. The serialized
                        // write alone leaves the binding empty until a
                        // scene reload. Safe: the crash guard already
                        // cleared this controller.
                        animator.Rebind();
                        ctx.Report.Approximated(Category,
                            "Controller linked through the serialized property",
                            "Unity's Animator API refused this assignment (it stores null, " +
                            "silently, for reasons it does not report), so the reference was " +
                            "written the way the Inspector writes it instead, and the Animator " +
                            "rebound to pick it up. The saved prefab carries the correct " +
                            "controller either way.");
                    }

                    // Judged on the serialized value, not the getter.
                    // The getter can lag within the same editor frame.
                    // The serialized value is what CVR loads.
                    var verify = new SerializedObject(animator);
                    var heldReference = verify.FindProperty("m_Controller").objectReferenceValue;
                    if (heldReference != overrides)
                    {
                        // Carry what the read-back saw into the report.
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

                        // Cleared through the same serialized path.
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

        static HashSet<string> SliderParameterNames(BridgeContext ctx)
        {
            var names = new HashSet<string>();
            var settings = ctx.CvrAvatar != null && ctx.CvrAvatar.avatarSettings != null
                ? ctx.CvrAvatar.avatarSettings.settings
                : null;
            if (settings == null)
            {
                return names;
            }
            foreach (var entry in settings)
            {
                if (entry != null && entry.setting is CVRAdvancesAvatarSettingSlider
                    && !string.IsNullOrEmpty(entry.machineName))
                {
                    names.Add(entry.machineName);
                }
            }
            return names;
        }

        static void FillEmptyMotionSlots(BridgeContext ctx, AnimatorController master)
        {
            AnimationClip filler = null;
            int filled = 0;
            int states = 0;
            int emptied = 0;
            var seen = new HashSet<Motion>();
            var sliders = new SortedSet<string>(StableSampleOrder.Instance);
            var sliderParameters = SliderParameterNames(ctx);

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

                // A slider's holes are dropped rather than filled.
                // Unity blends a 1D tree between the two children either
                // side of the parameter. A null child never takes weight.
                // A filler clip does, and it animates nothing.
                //
                // Slider only. A dropdown's empty option is a deliberate
                // "show nothing" the parameter lands exactly on.
                //
                // Thresholds are pinned first. Removing a child only
                // renumbers neighbours while useAutomaticThresholds is on.
                if (tree.blendType == BlendTreeType.Simple1D
                    && !string.IsNullOrEmpty(tree.blendParameter)
                    && sliderParameters.Contains(tree.blendParameter))
                {
                    var kept = new List<ChildMotion>();
                    foreach (var child in children)
                    {
                        if (child.motion != null && !IsCurveless(child.motion, filler))
                        {
                            kept.Add(child);
                        }
                    }
                    int dropped = children.Length - kept.Count;
                    if (dropped > 0 && kept.Count >= 2)
                    {
                        foreach (var child in kept)
                        {
                            Walk(child.motion);
                        }
                        tree.useAutomaticThresholds = false;
                        tree.children = kept.ToArray();
                        emptied += dropped;
                        sliders.Add(tree.blendParameter);
                        return;
                    }
                }

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
                        // The state's own motion counts too. Unity derives
                        // state duration from it while building the graph.
                        // A motionless state is the same hole as an empty
                        // blend tree slot.
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

            if (emptied > 0)
            {
                ctx.Report.Converted(Category,
                    $"{emptied} empty slot(s) removed from {sliders.Count} slider tree(s) instead of filled",
                    $"{string.Join(", ", sliders)} — a slider blends between the two children either side " +
                    "of its value, and an empty slot is not one of them: the weight passes across the gap " +
                    "to the real motions, which is how a few clips spread over many slots still travel " +
                    "smoothly. Filling those slots with a placeholder would make it an eligible neighbour " +
                    "that animates nothing, and the slider would do nothing until its value reached a real " +
                    "clip — reported from an avatar whose size slider did nothing until 0.97 and then " +
                    "jumped. Every surviving motion keeps the threshold the author gave it. Dropdowns and " +
                    "toggles are left alone: their empty options are deliberate, and the parameter lands " +
                    "on them rather than between them.");
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

        static bool TryOperator(out AnimatorDriverTask.Operator op, params string[] names)
        {
            foreach (string name in names)
            {
                if (System.Enum.IsDefined(typeof(AnimatorDriverTask.Operator), name))
                {
                    op = (AnimatorDriverTask.Operator)System.Enum.Parse(
                        typeof(AnimatorDriverTask.Operator), name);
                    return true;
                }
            }
            op = default;
            return false;
        }
        static void ReportSkippedLayers(BridgeContext ctx)
        {
            if (ctx.SourceDescriptor?.baseAnimationLayers == null)
            {
                return;
            }
            foreach (var layer in ctx.SourceDescriptor.baseAnimationLayers)
            {
                bool wanted;
                string option;
                switch (layer.type)
                {
                    case VRCAvatarDescriptor.AnimLayerType.Base:
                        wanted = ctx.Settings.convertBaseLayer; option = "Base / locomotion"; break;
                    case VRCAvatarDescriptor.AnimLayerType.Additive:
                        wanted = ctx.Settings.convertAdditiveLayer; option = "Additive"; break;
                    case VRCAvatarDescriptor.AnimLayerType.Gesture:
                        wanted = ctx.Settings.convertGestureLayer; option = "Gesture (hand poses)"; break;
                    case VRCAvatarDescriptor.AnimLayerType.Action:
                        wanted = ctx.Settings.convertActionLayer; option = "Action (emotes, AFK)"; break;
                    case VRCAvatarDescriptor.AnimLayerType.FX:
                        wanted = ctx.Settings.convertFxLayer; option = "FX (toggles, expressions)"; break;
                    default: continue;
                }
                if (wanted || layer.isDefault
                    || !(layer.animatorController is AnimatorController controller)
                    || controller.layers.Length == 0)
                {
                    continue;
                }

                int drivers = 0, states = 0;
                foreach (var sub in controller.layers)
                {
                    WalkMachines(sub.stateMachine, machine =>
                    {
                        foreach (var child in machine.states)
                        {
                            states++;
                            foreach (var behaviour in child.state.behaviours)
                            {
                                if (behaviour is VRCAvatarParameterDriver)
                                {
                                    drivers++;
                                }
                            }
                        }
                    });
                }

                ctx.Report.Warning(Category,
                    $"The avatar's own {layer.type} layer is NOT being converted",
                    $"\"{controller.name}\" — {controller.layers.Length} layer(s), {states} state(s)" +
                    (drivers > 0 ? $" and {drivers} parameter driver(s)" : "") +
                    ". This is the avatar's own animator content, and it is being left behind " +
                    "entirely: not merged, not carried at zero weight, absent. Anything it drove " +
                    "is gone with it" +
                    (drivers > 0
                        ? " — and those parameter drivers are how a sequence sets its own state, so " +
                          "a feature whose menu toggle and mesh swaps live in FX can still switch ON " +
                          "and then have nothing left to switch it back off. An avatar that " +
                          "transforms is the usual shape of this."
                        : ".") +
                    $" Tick \"{option}\" and convert again if this layer matters.");
            }
        }

        internal static List<(VRCAvatarDescriptor.AnimLayerType id, AnimatorController controller)> GetSelectedVrcControllers(BridgeContext ctx)
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
                // Names, types and order match the CCK's own controller.
                // CVR dispatches writes on the declared type, so these
                // must match the real thing exactly.
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

            // Keeping GoGo means GoGo is the locomotion. Leaving the
            // CCK's Locomotion/Emotes underneath has the two fighting
            // for the body every frame. Accepted losses in this mode
            // are listed in the report warning below.
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
            // Rebuild discrete gesture checks as threshold bands on the
            // gesture floats, the idiom the CCK's own controller uses.
            // The integer Idx route looks equivalent in the decompile,
            // but the stock controller never references Idx.
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

            if (ConvertedPlayAudio.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{ConvertedPlayAudio.Count} animator-played audio player(s) wired to ChilloutVR",
                    string.Join("; ", ConvertedPlayAudio.Take(6)) +
                    (ConvertedPlayAudio.Count > 6 ? "; …" : "") +
                    ". VRChat plays these from a state behaviour ChilloutVR does not have. " +
                    "Looping sounds get a Play On Awake source whose enabled flag the state " +
                    "animates: entering switches the sound on, every other state in the layer " +
                    "switches it back off. One-shots play through ChilloutVR's own " +
                    "CVRAudioDriver instead: entering the state pulses the clip index, and the " +
                    "sound plays to completion however quickly the state exits.");
            }
            if (ApproximatedPlayAudio.Count > 0)
            {
                ctx.Report.Approximated(Category,
                    $"{ApproximatedPlayAudio.Count} audio playback detail(s) simplified",
                    string.Join("; ", ApproximatedPlayAudio.Take(8)) +
                    (ApproximatedPlayAudio.Count > 8 ? "; …" : "") +
                    ". A state behaviour could randomise clips, volume and pitch per play and " +
                    "delay the start; an enable window cannot. The sound still plays, with the " +
                    "fixed settings named here.");
            }
            if (DroppedPlayAudioCount > 0)
            {
                ctx.Report.Skipped(Category,
                    $"{DroppedPlayAudioCount} animator-driven audio player(s) removed",
                    string.Join("; ", DroppedPlayAudio) + (DroppedPlayAudioCount > DroppedPlayAudio.Count ? "; …" : "") +
                    " — these could not be wired to a state window, for the reason named on each. " +
                    "The AudioSource is still on the avatar, so the sound can be wired by hand: " +
                    "a toggle animating the AudioSource's enabled flag with Play On Awake set " +
                    "plays and stops it, and ChilloutVR's own CVRAudioDriver can switch between " +
                    "clips from an animated index.");
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

        static readonly List<string> DroppedPlayAudio = new List<string>();
        static int DroppedPlayAudioCount;
        static readonly List<string> ConvertedPlayAudio = new List<string>();
        static readonly List<string> ApproximatedPlayAudio = new List<string>();
        static readonly Dictionary<(Motion motion, string path, float value), AnimationClip> AudioWindowClones
            = new Dictionary<(Motion, string, float), AnimationClip>();
        static readonly List<string> DroppedPoseSpace = new List<string>();
        static int DroppedPoseSpaceCount;

        // VRCAnimatorLayerControl, captured before the behaviours are
        // destroyed so the weight gates they implemented can be rebuilt.
        // Keys are "<playable>:<source layer index>"; the enum names
        // match across VRChat's two layer enums.
        static readonly Dictionary<string, string> MergedLayerNames =
            new Dictionary<string, string>();
        static readonly List<(string key, float goal)> CapturedLayerControls =
            new List<(string, float)>();

        // VRChat plays audio from a state behaviour; CVR has none.
        // The window approximation: the AudioSource plays on awake and
        // the state that carried the behaviour animates its enabled
        // flag. The restore passes assert the off in every other state.
        static void ConvertPlayAudio(BridgeContext ctx, AnimatorState state,
            VRC.SDK3.Avatars.Components.VRCAnimatorPlayAudio playAudio)
        {
            string where = $"state \"{state?.name ?? "(machine)"}\" -> \"{playAudio.SourcePath}\"";
            void Drop(string reason)
            {
                if (DroppedPlayAudio.Count < 6)
                {
                    DroppedPlayAudio.Add($"{where} ({reason})");
                }
                DroppedPlayAudioCount++;
            }

            var host = string.IsNullOrEmpty(playAudio.SourcePath)
                ? null : ctx.Target.transform.Find(playAudio.SourcePath);
            var source = host != null ? host.GetComponent<AudioSource>() : null;
            if (source == null)
            {
                Drop("no AudioSource at that path");
                return;
            }
            if (state == null)
            {
                Drop("sits on a state machine, not a state");
                return;
            }
            bool play = playAudio.PlayOnEnter;
            if (!play && !playAudio.StopOnEnter)
            {
                Drop("only acts on state exit");
                return;
            }
            if (state.motion is BlendTree)
            {
                Drop("the state plays a blend tree");
                return;
            }

            string path = playAudio.SourcePath;
            bool effectiveLoop = source.loop;
            var clips = (playAudio.Clips ?? new AudioClip[0]).Where(c => c != null).Distinct().ToList();
            if (play)
            {
                // Enum names, not values: NeverApply means the source keeps its own setting.
                bool Apply(System.Enum setting) => setting.ToString() != "NeverApply";
                if (Apply(playAudio.ClipsApplySettings) && clips.Count > 0)
                {
                    source.clip = clips[0];
                    if (clips.Count > 1)
                    {
                        ApproximatedPlayAudio.Add(
                            $"{where}: played one of {clips.Count} clips ({playAudio.PlaybackOrder}); plays \"{clips[0].name}\" here");
                    }
                }
                if (Apply(playAudio.LoopApplySettings))
                {
                    source.loop = playAudio.Loop;
                }
                effectiveLoop = source.loop;
                if (Apply(playAudio.VolumeApplySettings))
                {
                    source.volume = (playAudio.Volume.x + playAudio.Volume.y) * 0.5f;
                    if (!Mathf.Approximately(playAudio.Volume.x, playAudio.Volume.y))
                    {
                        ApproximatedPlayAudio.Add(
                            $"{where}: volume was randomised {playAudio.Volume.x:0.##}..{playAudio.Volume.y:0.##}; fixed at the midpoint");
                    }
                }
                if (Apply(playAudio.PitchApplySettings))
                {
                    source.pitch = (playAudio.Pitch.x + playAudio.Pitch.y) * 0.5f;
                    if (!Mathf.Approximately(playAudio.Pitch.x, playAudio.Pitch.y))
                    {
                        ApproximatedPlayAudio.Add(
                            $"{where}: pitch was randomised {playAudio.Pitch.x:0.##}..{playAudio.Pitch.y:0.##}; fixed at the midpoint");
                    }
                }
                if (playAudio.DelayInSeconds > 0f)
                {
                    ApproximatedPlayAudio.Add($"{where}: {playAudio.DelayInSeconds:0.##} s start delay dropped");
                }
                if (playAudio.PlayOnExit || playAudio.StopOnExit)
                {
                    ApproximatedPlayAudio.Add($"{where}: exit actions dropped; the sound follows the state instead");
                }
            }

            // A one-shot must play to completion, and an enable window
            // on a momentary state cuts it to a click. The CCK's own
            // CVRAudioDriver plays on an index change and lets the clip
            // finish, so the state pulses the index instead: 0 on entry,
            // back to -1 a frame later, rearmed for the next entry.
            if (play && !effectiveLoop)
            {
                source.playOnAwake = false;
                source.enabled = true;
                var pulse = host.GetComponent<CVRAudioDriver>();
                if (pulse == null)
                {
                    pulse = host.gameObject.AddComponent<CVRAudioDriver>();
                }
                pulse.audioSource = source;
                if (clips.Count > 0 && (pulse.audioClips == null || pulse.audioClips.Count == 0))
                {
                    pulse.audioClips = new List<AudioClip>(clips);
                }
                pulse.selectedAudioClip = -1;
                pulse.playOnSwitch = true;
                EditorUtility.SetDirty(pulse);
                state.motion = AudioPulseMotion(state.motion as AnimationClip, state.name, path);
                EditorUtility.SetDirty(state);
                ConvertedPlayAudio.Add($"{where} (one-shot, audio driver pulse)");
                return;
            }
            if (play)
            {
                source.playOnAwake = true;
            }

            // The avatar's own wiring wins where it exists. A clip that
            // already toggles the object or the component is the gate;
            // only the source setup above is needed then.
            var current = state.motion as AnimationClip;
            bool drivesActive = false, drivesEnabled = false;
            if (current != null)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(current))
                {
                    if (binding.path != path)
                    {
                        continue;
                    }
                    drivesActive |= binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive";
                    drivesEnabled |= binding.type == typeof(AudioSource) && binding.propertyName == "m_Enabled";
                }
            }
            if (play && drivesActive)
            {
                source.enabled = true;
                ConvertedPlayAudio.Add($"{where} (object toggle already wired)");
                return;
            }
            if (drivesEnabled)
            {
                if (play)
                {
                    source.enabled = false;
                }
                ConvertedPlayAudio.Add($"{where} (enable curve already wired)");
                return;
            }

            float value = play ? 1f : 0f;
            if (play)
            {
                source.enabled = false;
            }
            state.motion = AudioWindowMotion(current, state.name, path, value);
            EditorUtility.SetDirty(state);
            ConvertedPlayAudio.Add(where);
        }

        // The pulse rearms itself: frame 0 writes the index, the next
        // frame writes -1, so re-entering the state plays again even
        // when nothing else animates the driver.
        static Motion AudioPulseMotion(AnimationClip current, string stateName, string path)
        {
            var key = ((Motion)current, "pulse:" + path, 0f);
            if (AudioWindowClones.TryGetValue(key, out var cached))
            {
                return cached;
            }
            AnimationClip clone;
            if (current != null)
            {
                clone = UnityEngine.Object.Instantiate(current);
                clone.name = current.name + " audio";
            }
            else
            {
                clone = new AnimationClip { name = SanitizeFileName($"{stateName} audio") };
            }
            clone.hideFlags = HideFlags.None;
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f) { outTangent = float.PositiveInfinity },
                new Keyframe(1f / 60f, -1f) { inTangent = float.PositiveInfinity });
            AnimationUtility.SetEditorCurve(clone,
                EditorCurveBinding.FloatCurve(path, typeof(CVRAudioDriver), "selectedAudioClip"),
                curve);
            AudioWindowClones[key] = clone;
            return clone;
        }

        static Motion AudioWindowMotion(AnimationClip current, string stateName, string path, float value)
        {
            var key = ((Motion)current, path, value);
            if (AudioWindowClones.TryGetValue(key, out var cached))
            {
                return cached;
            }
            AnimationClip clone;
            float length;
            if (current != null)
            {
                clone = UnityEngine.Object.Instantiate(current);
                clone.name = current.name + " audio";
                length = Mathf.Max(current.length, 1f / 60f);
            }
            else
            {
                clone = new AnimationClip { name = SanitizeFileName($"{stateName} audio") };
                length = 1f / 60f;
            }
            clone.hideFlags = HideFlags.None;
            AnimationUtility.SetEditorCurve(clone,
                EditorCurveBinding.FloatCurve(path, typeof(AudioSource), "m_Enabled"),
                AnimationCurve.Constant(0f, length, value));
            AudioWindowClones[key] = clone;
            return clone;
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
                else if (behaviour is VRC.SDK3.Avatars.Components.VRCAnimatorPlayAudio playAudio)
                {
                    ConvertPlayAudio(ctx, state, playAudio);
                    UnityEngine.Object.DestroyImmediate(behaviour, true);
                }
                else if (behaviour is VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl layerControl
                         && layerControl.playable != VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.Action)
                {
                    // Action gates feed the emote transplant, which reads
                    // the source layers itself; these are the FX-style
                    // reaction gates, kept for the weight revival pass.
                    CapturedLayerControls.Add(($"{layerControl.playable}:{layerControl.layer}",
                        layerControl.goalWeight));
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

        // Running totals so the report gets one line, not one per behaviour.
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
            // Slashes and spaces in parameter names break the CCK's
            // controller autogeneration. Rename from the menu label,
            // consistently everywhere.
            var sanitizedNames = new Dictionary<string, string>();
            var takenNames = new HashSet<string>(master.parameters.Select(p => p.name));

            // Renames decided while the menu was built. A Joystick2D
            // addresses its axes as "<machineName>-x"/"-y", so those
            // exact names must exist.
            //
            // Consulted before ParameterRenameMap, so entries shadow the
            // core translations. Never admit a key the game drives, or a
            // value colliding with an existing parameter. Skip and report.
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
                // Stream-fed parameters must stay synced. The stream runs
                // on the wearer's copy only, and a "#" name never
                // replicates, so remote viewers would see frozen defaults.
                bool preserved = CvrCoreParameters.Contains(result) ||
                                 StreamFedParameters.Contains(result) ||
                                 ctx.PreserveParameters.Contains(name) ||
                                 ctx.PreserveParameters.Contains(result) ||
                                 ctx.ContactParameters.Contains(name);
                // Already-local names are left alone; no "##" doubling.
                if (ctx.Settings.preserveParameterSyncState && !preserved
                    && !result.StartsWith("#", StringComparison.Ordinal))
                {
                    result = "#" + result;
                }
                return result;
            }

            // The only property names a clip may have rewritten.
            // Captured before renaming, so these are pre-rename spellings.
            // Muscle curves share the Animator binding shape with animated
            // parameters; without this filter they would get "#" prefixes
            // and bind to nothing.
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
            // Every state machine, not just the merged VRChat layers.
            // A reference outside vrcLayers must follow the rename too,
            // or it addresses a parameter that no longer exists.
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

        static void RestorePartialOffStates(AnimatorController master, BridgeContext ctx,
            HashSet<string> writtenPaths)
        {
            var topped = new List<string>();
            int curvesAdded = 0;

            // Which layer owns each property. A binding two layers
            // animate belongs to neither: dual restores fight, and the
            // loser's toggle breaks. FillEmptyStatesWithRestoreClips
            // applies the same rule, so its omissions are deliberate.
            var owner = new Dictionary<EditorCurveBinding, int>();
            var contested = new HashSet<EditorCurveBinding>();
            for (int i = 0; i < master.layers.Length; i++)
            {
                var l = master.layers[i];
                if (l?.stateMachine == null) continue;
                var here = new HashSet<EditorCurveBinding>();
                // Same reachability rule as BuildRestoreOwnership.
                // Orphan states never play, so they own nothing.
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
                    continue;   // larger than two states is a machine, not a toggle
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
                // Same rule as the assertion pass's copies: settings travel with the curves, or a
                // looping source comes back loop=False and freezes after one play.
                AnimationUtility.SetAnimationClipSettings(filled,
                    AnimationUtility.GetAnimationClipSettings(offClip));
                filled.frameRate = offClip.frameRate;
                filled.wrapMode = offClip.wrapMode;
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
                        continue;   // parameters, not properties; nothing to restore
                    }
                    if (contested.Contains(binding) || !owner.TryGetValue(binding, out int owns)
                        || owns != layerIndex)
                    {
                        continue;   // another layer animates it too; leave arbitration to it
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
                // Delete anything already at the path first. CreateAsset
                // over an existing asset replaces the object and leaves
                // other states holding null motions.
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(target) != null)
                {
                    AssetDatabase.DeleteAsset(target);
                }
                AssetDatabase.CreateAsset(filled, target);
                // Register with the shared sweep-survivor list, or the
                // filler's stale-clip sweep deletes this file.
                writtenPaths.Add(target);
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

        static void AssertOwnedBindingsEverywhere(AnimatorController master, BridgeContext ctx)
        {
            BuildRestoreOwnership(master.layers, out var owner, out _, out var treeDriven, out _);

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

                // What this layer animates from plain clip states.
                // Tree-driven bindings are exempt; ownership never holds them.
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
                    && !TreeDrivenElsewhere(treeDriven, b, layerIndex)
                    && owner.TryGetValue(b, out int by) && by == layerIndex).ToList();
                var ownedObjects = stateObjects.Where(b => !TreeDrivenElsewhere(treeDriven, b, layerIndex)
                    && owner.TryGetValue(b, out int by) && by == layerIndex).ToList();
                if (ownedFloats.Count == 0 && ownedObjects.Count == 0)
                {
                    continue;
                }

                int addedThisLayer = 0;
                foreach (var state in states)
                {
                    // A blend tree stands aside. A constant assertion
                    // would fight the parameter-driven value.
                    //
                    // Routers do not stand aside. Ownership is silence at
                    // the top, authority at the bottom, and that only
                    // works while the bottom speaks. The conversion-time
                    // value is written for bindings this layer owns.
                    if (state.motion is BlendTree)
                    {
                        continue;
                    }
                    // Empty states are filled too, under the same guards:
                    // owned bindings only, never tree-driven. The later
                    // placeholder from FillEmptyMotionSlots asserts
                    // nothing, so this must speak first.
                    var current = state.motion as AnimationClip;
                    var haveFloats = current != null
                        ? new HashSet<EditorCurveBinding>(AnimationUtility.GetCurveBindings(current))
                        : new HashSet<EditorCurveBinding>();
                    var haveObjects = current != null
                        ? new HashSet<EditorCurveBinding>(AnimationUtility.GetObjectReferenceCurveBindings(current))
                        : new HashSet<EditorCurveBinding>();

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
                        if (current == null)
                        {
                            return copy;   // nothing to carry over; the state held no motion
                        }
                        // Settings travel with the curves or the copy changes behaviour: an
                        // 8.3-second looping animation topped by this pass came back loop=False,
                        // playing once and freezing where the original cycled. Caught by the
                        // corpus gate before release, on the first avatar checked.
                        AnimationUtility.SetAnimationClipSettings(copy,
                            AnimationUtility.GetAnimationClipSettings(current));
                        copy.frameRate = current.frameRate;
                        copy.wrapMode = current.wrapMode;
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

        static void ReportNoWdCoverage(AnimatorController master, BridgeContext ctx)
        {
            BuildRestoreOwnership(master.layers, out var owner, out _, out var treeDriven, out _);

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
                        || TreeDrivenElsewhere(treeDriven, pair.Key, layerIndex))
                    {
                        continue;
                    }
                    // A binding that no longer resolves is a toggle over
                    // a ghost; its object went with a stripped system.
                    // Not a coverage gap.
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

            // A driver reads as well as writes. Operand types are stamped
            // when the task is built, before ParameterTypeInference may
            // retype. A mismatched read logs a warning and returns 0.
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
                                    // Greater/Less read against the threshold.
                                    // A bool only reads 0 or 1, so an
                                    // out-of-range comparison is a tautology,
                                    // never an If/IfNot.
                                    //
                                    // VRCFury writes remote branches as a
                                    // float band meaning "IsLocal is 0".
                                    // Misread, its NonLocal states become
                                    // unreachable on every copy.
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
                            // Unsatisfiable: "> 1" or "< 0" on a 0/1 value.
                            // Drop the transition. VRCFury encodes OR as a
                            // condition pair; only one twin can fire, and
                            // the dead one goes.
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

        static readonly System.Text.RegularExpressions.Regex IllegalMenuNameChars =
            new System.Text.RegularExpressions.Regex(@"[^a-zA-Z0-9/\-_#]");

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

            // Hold the array. The parameters property hands back a fresh
            // copy on every read, so property-iterate-then-assign throws
            // the renames away.
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

        static void PruneDeadMenuEntries(AnimatorController master, BridgeContext ctx)
        {
            var settings = ctx.CvrAvatar.avatarSettings.settings;
            if (settings == null || settings.Count == 0)
            {
                return;
            }
            var known = new HashSet<string>(master.parameters.Select(p => p.name));
            // A parameter can exist and still be inert: nothing
            // conditions on it, nothing reads or writes it.
            var referenced = CollectReferencedParameters(master);

            // A joystick's machineName is not itself a parameter. CVR
            // derives one per axis, "<machineName>-x"/"-y"/"-z", so the
            // bare name never appears in the animator.
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
                    // Used as a quantity somewhere; renumbering would
                    // change behaviour. The placeholders stay, reported.
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

        // How a dropdown parameter is used: exact matches and
        // the boundaries it is compared across.
        class SelectorUse
        {
            public readonly HashSet<int> Exact = new HashSet<int>();
            public readonly HashSet<int> GreaterCuts = new HashSet<int>();  // "> t"
            public readonly HashSet<int> LessCuts = new HashSet<int>();     // "< t"
        }

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
            // Deliberately not declared. The repair is the rename: no
            // dangling field may be blank, since a blank name crashes
            // the graph builder. Declaring them for real brings a
            // reproducible crash back. Measured twice; do not redeclare
            // without measuring again.
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

        static void WithdrawSelfDrivenExposures(AnimatorController master, BridgeContext ctx)
        {
            var settings = ctx.CvrAvatar != null && ctx.CvrAvatar.avatarSettings != null
                ? ctx.CvrAvatar.avatarSettings.settings : null;
            if (settings == null || ctx.AutoExposedParameters.Count == 0)
            {
                return;
            }

            // Everything a driver writes in the merged controller.
            // The CCK AnimatorDriver, not the VRChat one; BehaviourPass
            // already converted every VRCAvatarParameterDriver.
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
                // Nothing safely strippable. Leave the avatar's own
                // system; a native blink on top doubles up the eyes.
                //
                // Fill the shape slots anyway. Inert while the tickbox
                // is off, and detection already picked them. The fix
                // becomes the one tick the report names.
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

            // CVR's blink is not an animation layer. The client writes
            // the weight in LateUpdate, after the animator, and owns
            // the shape outright. A shape an expression still animates
            // would be flattened for good.
            //
            // When the removed layer's shape is contested, move the
            // native blink onto a family nothing else drives.
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
            // Live weights may be whatever the old system last wrote.
            // Native blink owns these now, and its rest is open.
            // The removed layer's shape is zeroed too.
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

        static HashSet<string> LayerOffStates(AnimatorControllerLayer source)
            => LayerWeightStates(source, on: false);

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
                        // Both behaviour types count, Action playable only.
                        // FX or Gesture weights say nothing about this
                        // layer's visibility.
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
                    // Pose states get the clip without baked travel;
                    // that would displace the camera with no input.
                    // Root motion that returns home is kept.
                    // keepVertical: lowering the body is height change,
                    // not travel. Rotation is dropped; the client's
                    // capsule is always upright.
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

                // Arming: every outside entry into the window becomes an
                // AnyState transition with the same conditions.
                // Unconditional entries would fire every frame; skipped.
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
                    // From the locomotion layer's resting state, never
                    // AnyState. AnyState re-fires from inside the window
                    // and the machine loops visibly. Arming from rest
                    // reproduces the source's positional guarantee:
                    // nothing re-fires until hand-back plus a fresh rise.
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

                // ---- edge-triggered arming ----
                // Arming on a level replays forever: a dropdown holds its
                // value, so a played-once pose re-enters on hand-back.
                // Entry must fire on the rise of the conditions, once.
                //
                // A "#" ready flag gates every arming transition, run by
                // a weight-0 memory layer: Ready raises it, Engaged drops
                // it while conditions hold, Ready re-raises only after
                // they go false. The window evaluates first, so one frame
                // of flag-up arms once per rise.
                string readyName = "#AB_Ready_" + SanitizeParameterName(clone.name);
                if (master.parameters.All(p => p.name != readyName))
                {
                    var withReady = master.parameters.ToList();
                    withReady.Add(new AnimatorControllerParameter
                    {
                        name = readyName,
                        // Disarmed at load; see the Rest prologue below.
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

                    // Capture: drop the flag and remember the armed values.
                    // Separate state; Engaged re-enters itself every tick,
                    // and capturing there would re-baseline every cycle.
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

                    // Release when the arming conditions go false, one
                    // transition per negation, or when any armed
                    // parameter changes while they stay true. The change
                    // release lets a held menu switch emotes directly.
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
                // ---- disarmed until something actually changes ----
                // Arming conditions can be true at load. A VRChat Action
                // layer sits at weight 0, so its conditions may hold
                // permanently; the transplant has no weight gate.
                //
                // The machine starts disarmed, snapshots the values it
                // woke with, and arms only when one departs from that
                // snapshot: a real user action, not the avatar existing.
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

        static readonly HashSet<string> EmotePlayerParameters = new HashSet<string>
        {
            "VRCEmote", "VRCFaceBlendH", "VRCFaceBlendV", "AFK", "Seated", "InStation",
            "IsLocal", "Upright", "Grounded", "Supine", "Voice", "Sitting", "TrackingType",
        };

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
            // Muscle curves bind like animated animator parameters.
            // Requiring a declared name separates them; a clip writing
            // an undeclared parameter does nothing anyway.
            var declaredNames = new HashSet<string>(master.parameters.Select(p => p.name));

            void NoteMotion(Motion motion)
            {
                if (motion is BlendTree tree)
                {
                    // Only the fields this blend type actually reads.
                    // "Blend" survives as a leftover on Direct trees and
                    // blendParameterY is ignored by 1D trees. Counting
                    // vestigial fields invents phantom references.
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

                // AvatarUpright matches VRChat's Upright exactly.
                // Same range, same meaning, no conversion needed.
                (bare: "Upright", streamType: "AvatarUpright", app: "Override", lo: 0f, hi: 0f),

                // CVR only reports whether full-body tracking is on, so
                // the six VRChat states collapse to 3 and 6. Both above
                // zero, which is what most avatars test for.
                // VelocityX/Y/Z are client core parameters; never
                // streamed. VelocityMagnitude is derived in the animator.
                (bare: "TrackingType", streamType: "LocalPlayerFullBodyEnabled", app: "Remap", lo: 3f, hi: 6f),

                // AvatarHeight is the calibrated height in metres, the
                // closest reading of EyeHeightAsMeters. The rest of the
                // scale family derives from it in FeedScaleParameters.
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

            // The serialized field is an int the client reads back.
            // When the installed CCK's enum predates a name, writing the
            // client's numeric value still round-trips. The client's
            // numbers are the contract.
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
                // TargetType.Animator is the CCK's "Sub Animator" and
                // needs a nominated target. AvatarAnimator is the
                // avatar's own animator, which is what these need.
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
                        // Only states that hold still lose the flag.
                        // The every-frame restart pins a state at frame 0.
                        // For a real animated clip that pin is shipped
                        // behaviour. The strobe only ever comes from
                        // constant or conversion-added motions, where pin
                        // and play are identical.
                        var destination = transition.destinationState;
                        if (destination != null && !MotionHoldsStill(destination.motion))
                        {
                            continue;
                        }
                        // A destination with an exit-time transition
                        // depends on the restart to stay alive; every
                        // re-entry resets the clock. Clearing the flag
                        // would set it cycling at clip length.
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
                // Squared distance from 1, compared against one percent
                // squared. Exact equality on a streamed float flickers.
                tasks.Add(Task(Delta, Float, AnimatorDriverTask.Operator.Subtraction,
                    Factor, 0f, null, 1f));
                tasks.Add(Task(Delta, Float, AnimatorDriverTask.Operator.Multiplication,
                    Delta, 0f, Delta, 0f));
                // Resolved by name. The CCK has shipped both spellings,
                // "MoreThan" and "MoreThen"; naming either directly
                // fails to compile against the other.
                if (TryOperator(out var moreThan, "MoreThan", "MoreThen"))
                {
                    tasks.Add(Task(modified.name,
                        modified.type == AnimatorControllerParameterType.Bool
                            ? AnimatorDriverTask.ParameterType.Bool
                            : Float,
                        moreThan,
                        Delta, 0f, null, 0.0001f));
                }
                else
                {
                    ctx.Report.Warning(Category, $"Scale feed for \"{modified.name}\" is incomplete",
                        "This ChilloutVR CCK spells its animator-driver comparison operators in a " +
                        "way AvatarBridge does not recognise, so the step that decides whether the " +
                        "avatar has been resized could not be built. The parameter still exists and " +
                        "everything else converts; it simply will not update itself when you scale. " +
                        "Please report the CCK version — this needs one more spelling added.");
                }
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

            // A 1/60 s clip targeting an undeclared parameter. It gives
            // the state a length, so the exit-time self-transition
            // cycles and the enter tasks re-run.
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
                // Embedded in something else. Clone the object itself,
                // not its whole container.
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

            // A material is not a leaf. VRCFury repacks textures into
            // temp containers, so a rescued material can still point
            // slots at something about to be deleted. That renders as
            // an untextured wash, not magenta.
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
                // No asset path means an in-memory object. A Fury bake
                // that errors partway leaves clips unsaved and DontSave;
                // Unity refuses those at save time and references dangle.
                // Anything not an asset must be cloned to survive saving.
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

            // Copying the clip is only half of it. A material-swap clip
            // carries the material as an object-reference curve, and the
            // cloned reference still points into Fury's temp. The toggle
            // then breaks the moment it is used, mesh gone magenta.
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
                    // A volatile tree must be cloned, not repaired in
                    // place. The controller must never keep referencing
                    // an asset inside Fury's temp folder.
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
                            // Same threshold discipline as AnimatorDeepCopier.
                            // Unity clamps child thresholds into [min,max]
                            // on assignment, and manual trees ship 0/0.
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

        static readonly HashSet<string> MuscleCurveNames = new HashSet<string>(HumanTrait.MuscleName.Select(name =>
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^(Left|Right) (Thumb|Index|Middle|Ring|Little) (.*)$");
            return match.Success ? $"{match.Groups[1].Value}Hand.{match.Groups[2].Value}.{match.Groups[3].Value}" : name;
        }));

        static bool IsFingerCurve(string property) => property.Contains("Hand.");

        static bool IsRootCurve(string property) =>
            property.StartsWith("RootT") || property.StartsWith("RootQ") ||
            property.StartsWith("MotionT") || property.StartsWith("MotionQ");

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
                // Everything the masking pass would act on: every merged
                // layer that does not deliberately animate the body.
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
                // A [Base] layer is masked even when it animates the
                // body. Merged, it lands above CVR's Locomotion/Emotes
                // on Override at weight 1, so it can only replace the
                // locomotion, never supplement it. Masked, the layer
                // keeps everything it can actually deliver and CVR's
                // locomotion stays authoritative.
                bool baseLayer = layer.name.StartsWith("[Base]", StringComparison.Ordinal);
                if (body && !baseLayer)
                {
                    continue; // deliberate body animation; reported separately
                }
                if (body)
                {
                    maskedBaseBody.Add(layer.name);
                }
                // Finger curves are blocked with the rest. VRChat's FX
                // playable cannot drive muscles, so those curves never
                // moved a finger there. A hands-only mask would let them
                // overwrite the gesture pose from above.
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

        static void UnmaskObjectSwapLayers(AnimatorController master, BridgeContext ctx)
        {
            var layers = master.layers;
            var freed = new SortedSet<string>(StableSampleOrder.Instance);
            var authored = new SortedSet<string>(StableSampleOrder.Instance);
            var conflicted = new SortedSet<string>(StableSampleOrder.Instance);
            bool changed = false;
            foreach (var layer in layers)
            {
                if (layer.avatarMask == null || !LayerSwapsObjectReferences(layer))
                {
                    continue;
                }
                if (!layer.avatarMask.name.StartsWith("AvatarBridge_", StringComparison.Ordinal))
                {
                    authored.Add(layer.name);
                    continue;
                }
                InspectLayerCurves(layer, out bool body, out bool fingers);
                if (body || fingers)
                {
                    // Not this one. Unmasked, its humanoid curves would
                    // drive the rig the mask protects. Deleting the
                    // curves is not an option: clips are shared, and
                    // editing one in place damages every user of it.
                    conflicted.Add(layer.name);
                    continue;
                }
                layer.avatarMask = null;
                freed.Add(layer.name);
                changed = true;
            }
            if (changed)
            {
                master.layers = layers;
            }
            if (freed.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{freed.Count} material-swap layer(s) unmasked so every slot applies",
                    $"{string.Join(", ", freed)} — a masked layer silently applies only the first material " +
                    "slot of a swap in game, so these keep no mask. They drive no muscles, so locomotion " +
                    "and hand gestures are unaffected.");
            }
            if (authored.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{authored.Count} layer(s) swap materials from behind a mask you authored",
                    $"{string.Join(", ", authored)} — with an avatar mask on the layer, only the first " +
                    "material slot of a swap applies in game. If a swap from one of these layers misses " +
                    "slots, remove the layer's mask in the Animator window and reconvert.");
            }
            if (conflicted.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{conflicted.Count} layer(s) both swap materials and animate the rig",
                    $"{string.Join(", ", conflicted)} — these keep their mask, because without it their " +
                    "body or finger curves would fight the client's locomotion and hand poses. The cost " +
                    "is that only the first material slot of a swap in those layers applies in game. If " +
                    "one of them misses slots, move its material swap into a layer of its own — one that " +
                    "animates nothing else — and reconvert.");
            }
        }

        static bool LayerSwapsObjectReferences(AnimatorControllerLayer layer)
        {
            if (layer.stateMachine == null)
            {
                return false;
            }
            bool found = false;
            WalkMachines(layer.stateMachine, machine =>
            {
                foreach (var child in machine.states)
                {
                    if (found || child.state == null)
                    {
                        continue;
                    }
                    foreach (var clip in CollectClips(child.state.motion))
                    {
                        if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0)
                        {
                            found = true;
                            break;
                        }
                    }
                }
            });
            return found;
        }

        internal static void AuditHandPoseConflictsForTest(AnimatorController master, BridgeContext ctx)
            => AuditHandPoseConflicts(master, ctx);

        static void AuditHandPoseConflicts(AnimatorController master, BridgeContext ctx)
        {
            // Runs even when the gesture layer converts. The promoted
            // LeftHand/RightHand layers are the avatar's own, and a
            // layer merged in above them can still overwrite the
            // fingers they just posed. VRChat never shows this: its FX
            // playable cannot drive muscles at all.
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
                    // A layer that deliberately drives the body.
                    // Stripping its fingers is the user's call, not the tool's.
                    warned.Add(layer.name);
                    continue;
                }
                if (mask == null)
                {
                    // No mask at all, so nothing to edit. Only act when
                    // the layer really animates fingers; adding a mask
                    // is a bigger intervention than editing one.
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

        static void HoistContestedTreeToggles(AnimatorController master, BridgeContext ctx)
        {
            var hoisted = new List<string>();
            int originalLayerCount = master.layers.Length;

            for (int li = 0; li < originalLayerCount; li++)
            {
                var layer = master.layers[li];
                if (layer?.stateMachine == null || IsProtectedLayer(layer.name))
                {
                    continue;
                }

                var directStates = new List<AnimatorState>();
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        if (child.state?.motion is BlendTree bt && bt.blendType == BlendTreeType.Direct)
                        {
                            directStates.Add(child.state);
                        }
                    }
                });
                if (directStates.Count == 0)
                {
                    continue;
                }

                // What the layer animates OUTSIDE the toggle-shaped children, so a binding shared
                // with a radial or a gesture state never triggers a hoist.
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
                if (candidates.Count < 2)
                {
                    continue;   // a contest takes two
                }
                var toggleTrees = new HashSet<BlendTree>(candidates.Select(c => c.Tree));
                var outside = new HashSet<EditorCurveBinding>();
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
                var usage = new Dictionary<EditorCurveBinding, int>();
                foreach (var candidate in candidates)
                {
                    foreach (var binding in candidate.Floats.Concat(candidate.Objects))
                    {
                        usage.TryGetValue(binding, out int n);
                        usage[binding] = n + 1;
                    }
                }

                bool Contested(ToggleTree t) => t.Floats.Concat(t.Objects).Any(b =>
                    usage.TryGetValue(b, out int users) && users > 1);
                bool TouchesOutside(ToggleTree t) => t.Floats.Concat(t.Objects).Any(outside.Contains);

                foreach (var state in directStates)
                {
                    var direct = (BlendTree)state.motion;
                    var children = direct.children;
                    var keep = new List<ChildMotion>();
                    bool changed = false;

                    foreach (var child in children)
                    {
                        var subtree = child.motion as BlendTree;
                        var match = subtree != null
                            ? candidates.FirstOrDefault(c => c.Tree == subtree) : default;
                        bool restsAtFullWeight =
                            master.parameters.Any(p => p.name == child.directBlendParameter
                                                       && p.defaultFloat >= 1f);
                        if (match.Tree == null || !restsAtFullWeight
                            || !Contested(match) || TouchesOutside(match))
                        {
                            keep.Add(child);
                            continue;
                        }

                        string label = ToggleTreeLabel(match.Tree, layer.name);
                        master.AddLayer(SanitizeFileName($"{layer.name} {label}"));
                        var all = master.layers;
                        var lifted = all[all.Length - 1];
                        lifted.defaultWeight = 1f;
                        lifted.blendingMode = AnimatorLayerBlendingMode.Override;
                        lifted.avatarMask = layer.avatarMask;
                        var home = lifted.stateMachine.AddState(SanitizeFileName(label));
                        home.motion = match.Tree;
                        home.writeDefaultValues = true;
                        lifted.stateMachine.defaultState = home;
                        master.layers = all;

                        hoisted.Add(label);
                        changed = true;
                    }
                    if (changed)
                    {
                        direct.children = keep.ToArray();
                        EditorUtility.SetDirty(direct);
                    }
                }
            }

            if (hoisted.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{hoisted.Count} toggle(s) that share parts moved into their own layers",
                    $"{string.Join(", ", hoisted.Take(8))}{(hoisted.Count > 8 ? ", …" : "")} — " +
                    "blended into one tree, toggles that animate the same thing add up and fight " +
                    "over it, so neither could safely restore it and both stuck in game. As " +
                    "separate layers the top one wins while it acts and each restores on its own. " +
                    "If two are switched on at once, the one higher in the list decides the " +
                    "shared part — VRChat showed a mix of both, which ChilloutVR cannot express.");
            }
        }

        static readonly Dictionary<string, string> restoreVerdicts = new Dictionary<string, string>();

        static void FillEmptyStatesWithRestoreClips(AnimatorController master, BridgeContext ctx,
            HashSet<string> writtenPaths)
        {
            var root = ctx.Target.transform;
            string dir = $"{ctx.OutputDir}/RehomedAssets";
            restoreVerdicts.Clear();
            int filled = 0, layersTouched = 0, reused = 0, sharedSkipped = 0, candidates = 0, routers = 0;
            // Bindings that named something this avatar no longer has.
            // Counted so the failure branch can say which skip fired.
            int unresolved = 0;
            var unresolvedPaths = new SortedSet<string>(StableSampleOrder.Instance);
            var names = new List<string>();
            var keptClips = new HashSet<string>();
            // Layers whose empty states are structural rather than a toggle.s off half.
            var notToggles = new SortedSet<string>(StableSampleOrder.Instance);

            // Snapshot once. master.layers hands back fresh wrappers on
            // every access, so indexes only mean anything within one
            // call. Layer names are unique by construction; key on them.
            var layers = master.layers;
            var indexByName = new Dictionary<string, int>();
            for (int i = 0; i < layers.Length; i++)
            {
                if (!indexByName.ContainsKey(layers[i].name))
                {
                    indexByName[layers[i].name] = i;
                }
            }
            BuildRestoreOwnership(layers, out var owner, out var allClips, out var treeDriven, out _);

            // Every layer of the finished controller, not the merged-layer
            // list. A layer is still the avatar's toggle after
            // ToggleNativizer renames it.
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
                        // A curve-less clip is an empty state. VRCFury
                        // parks a shared empty clip rather than null.
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
                // Anything larger is a machine, and its empty states are
                // structural, not "off" halves. Erring toward doing
                // nothing is cheap: an unfilled off state behaves as it
                // did in VRChat. Erring the other way changes the avatar.
                if (stateCount != 2)
                {
                    restoreVerdicts[layer.name] = $"not a two-state toggle ({stateCount} states)";
                    notToggles.Add($"{layer.name} ({stateCount} states)");
                    continue;
                }
                candidates += empties.Count;

                var clip = new AnimationClip { name = SanitizeFileName($"{layer.name} restore") };
                int curves = 0, shared = 0;

                // Where several layers animate one property, only the
                // lowest may restore it. With the lower layer restoring
                // and the higher silent, every combination comes out
                // right. Silence at the top, authority at the bottom.
                int here = indexByName.TryGetValue(layer.name, out int found) ? found : -1;
                bool Owns(EditorCurveBinding binding)
                {
                    // A tree in another layer drives this: leave it to
                    // the tree. A sibling tree in this layer is not
                    // running while the layer rests here.
                    if (TreeDrivenElsewhere(treeDriven, binding, here))
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
                        // The clip names something this avatar no longer
                        // has. No current value, nothing to restore.
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

                // Prefer the avatar's own animation. A generated clip is
                // a last resort: an authored clip may carry curves and
                // timing a snapshot cannot know about.
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
                // A stable name per layer, uniquified only against this
                // run. Folder-wide uniquifying stacks numbered copies
                // on every reconversion.
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
                // Candidates but no fills is the shape of a bug in this
                // pass. A toggle that switches on and never off is the
                // symptom; report it rather than stay silent.
                if (candidates > 0)
                {
                    // Say why, with the numbers that answer the question.
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
            // Spare everything the shared registry knows about, not just
            // this pass's own files. writtenPaths carries the partial-off
            // topping's clips too.
            int stale = DeleteStaleRestoreClips(dir, writtenPaths);
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

        static bool TreeDrivenElsewhere(Dictionary<EditorCurveBinding, HashSet<int>> treeDriven,
            EditorCurveBinding binding, int askingLayer)
        {
            if (!treeDriven.TryGetValue(binding, out var owning))
            {
                return false;
            }
            foreach (int layer in owning)
            {
                if (layer != askingLayer)
                {
                    return true;
                }
            }
            return false;
        }

        static void BuildRestoreOwnership(AnimatorControllerLayer[] layers,
            out Dictionary<EditorCurveBinding, int> owner, out HashSet<AnimationClip> allClips,
            out Dictionary<EditorCurveBinding, HashSet<int>> treeDriven,
            out Dictionary<EditorCurveBinding, int> anyOwner)
        {
            // Locals rather than the out params directly: a lambda may not touch an out parameter,
            // and the walk below is a lambda.
            var owners = new Dictionary<EditorCurveBinding, int>();
            // Every clip the avatar already has, so an authored one can be preferred over a
            // generated one.
            var clips = new HashSet<AnimationClip>();
            // Bindings any blend tree animates. Ownership is awarded from
            // plain clip states only; tree-driven bindings are exempt
            // from state restores, since a constant assertion fights a
            // parameter-driven value whenever the tree is live.
            var trees = new Dictionary<EditorCurveBinding, HashSet<int>>();
            // Ownership over everything a layer animates, trees included.
            // The tree repair needs this full map; the plain-state map
            // reads "absent" as "nobody claims it".
            var anyOwners = new Dictionary<EditorCurveBinding, int>();
            int layerIndex = -1;
            foreach (var candidate in layers)
            {
                layerIndex++;
                var floatsHere = new HashSet<EditorCurveBinding>();
                var objectsHere = new HashSet<EditorCurveBinding>();
                var anythingHere = new HashSet<EditorCurveBinding>();
                var treesHere = new HashSet<EditorCurveBinding>();
                // Ownership only counts states Unity can actually reach.
                // A library layer must not claim properties it can never
                // play. Clips are still collected from everywhere; a
                // library is where authored clips worth reusing live.
                var onlyPlayable = LibraryDefaultState(candidate);
                WalkMachines(candidate.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        var motion = child.state != null ? child.state.motion : null;
                        CollectClips(motion, clips);
                        if (motion is BlendTree)
                        {
                            // Trees feed the exemption and any-ownership
                            // maps, never plain-state ownership.
                            CollectBindings(motion, treesHere, treesHere);
                            if (onlyPlayable == null || child.state == onlyPlayable)
                            {
                                CollectBindings(motion, anythingHere, anythingHere);
                            }
                            continue;
                        }
                        if (onlyPlayable == null || child.state == onlyPlayable)
                        {
                            CollectBindings(motion, floatsHere, objectsHere);
                            CollectBindings(motion, anythingHere, anythingHere);
                        }
                    }
                });
                // Which layer's tree drives it, not merely that one does.
                // Within one layer only one state plays at a time, so a
                // sibling tree is not running while the layer rests.
                // Across layers the exemption holds; those really blend.
                foreach (var binding in treesHere)
                {
                    if (!trees.TryGetValue(binding, out var owning))
                    {
                        trees[binding] = owning = new HashSet<int>();
                    }
                    owning.Add(layerIndex);
                }
                foreach (var binding in floatsHere.Concat(objectsHere))
                {
                    if (!owners.ContainsKey(binding))
                    {
                        owners[binding] = layerIndex;
                    }
                }
                foreach (var binding in anythingHere)
                {
                    if (!anyOwners.ContainsKey(binding))
                    {
                        anyOwners[binding] = layerIndex;
                    }
                }
            }
            owner = owners;
            allClips = clips;
            treeDriven = trees;
            anyOwner = anyOwners;
        }

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

        // A 1D blend tree shaped like a toggle: one child animates, the other is empty.
        struct ToggleTree
        {
            public BlendTree Tree;
            public int EmptyIndex;
            public HashSet<EditorCurveBinding> Floats;
            public HashSet<EditorCurveBinding> Objects;
        }

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
            BuildRestoreOwnership(layers, out var owner, out var allClips, out _, out var anyOwner);

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
                        // Cross-layer arbitration uses the any-ownership
                        // map, trees included; this pass's repairs live
                        // inside trees. Unknown binding here: nobody's,
                        // so this layer may restore it.
                        return !anyOwner.TryGetValue(binding, out int lowest) || lowest == here;
                    }

                    var clip = new AnimationClip { name = SanitizeFileName($"{label} restore") };
                    int curves = 0, shared = 0;
                    foreach (var binding in candidate.Floats)
                    {
                        // Fury's AAP trees animate parameters, not the
                        // avatar, in this same two-child shape.
                        // Snapshotting one would pin a computed value.
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

                    // Prefer the avatar's own animation, as the state pass does.
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

        static void CollectToggleTrees(Motion motion, HashSet<BlendTree> seen, List<ToggleTree> into)
        {
            if (!(motion is BlendTree tree) || !seen.Add(tree))
            {
                return;
            }
            var children = tree.children;
            // Two children is the toggle; more is a selector whose
            // "none" option is the empty child. At that option the tree
            // asserts nothing, and CVR fills no silence. The empty child
            // gets the union of its siblings' properties at rest.
            if (tree.blendType == BlendTreeType.Simple1D && children.Length >= 2)
            {
                int empty = -1;
                bool multipleEmpty = false;
                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i].motion == null || IsCurveless(children[i].motion, null))
                    {
                        // More than one empty child: there is no single "off" to repair, and the
                        // shape is structural rather than a toggle or selector.
                        if (empty >= 0)
                        {
                            multipleEmpty = true;
                            break;
                        }
                        empty = i;
                    }
                }
                if (empty >= 0 && !multipleEmpty)
                {
                    var floats = new HashSet<EditorCurveBinding>();
                    var objects = new HashSet<EditorCurveBinding>();
                    for (int i = 0; i < children.Length; i++)
                    {
                        if (i != empty)
                        {
                            CollectBindings(children[i].motion, floats, objects);
                        }
                    }
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

        static string ToggleTreeLabel(BlendTree tree, string layerName)
        {
            string name = tree != null ? tree.name : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = layerName;
            }
            return name.Length > 60 ? name.Substring(0, 60).TrimEnd() : name;
        }

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
                // " restore.anim" and " restore 4.anim" are the shapes this pass writes.
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
                    if (theirs == null || theirs.length == 0 || mine == null || mine.length == 0)
                    {
                        same = false;
                        break;
                    }
                    // Every key, not just the ends. A loop returns to
                    // where it began and would pass as a constant.
                    // The middle of the curve is the whole difference
                    // between a rest pose and an oscillation around one.
                    foreach (var key in theirs.keys)
                    {
                        if (!Mathf.Approximately(key.value, mine.keys[0].value))
                        {
                            same = false;
                            break;
                        }
                    }
                    if (!same)
                    {
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
                    // In keep-GoGo mode Base/Additive/Action are supposed
                    // to drive the body; warning about them is noise.
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

        internal static bool IsDoomedGeneratedPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            path = path.Replace('\\', '/');
            // Only the temp roots. A broad "com.vrcfury" prefix also
            // matches the installed package, whose GUIDs are stable.
            // The wiped folders are exactly these two:
            return path.StartsWith("Packages/com.vrcfury.temp", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Packages/nadena.dev.ndmf/__Generated", StringComparison.OrdinalIgnoreCase);
        }

        static void AuditSizeBlendshapes(AnimatorController master, BridgeContext ctx)
        {
            var chains = ctx.ConvertedPhysicsChains;
            if (chains == null || chains.Count == 0)
            {
                return;
            }
            var root = ctx.Target.transform;

            // chain name -> the shapes that resize it
            var driven = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var seen = new HashSet<AnimationClip>();

            // How far each shape travels across the whole controller,
            // not within one clip. A toggle is a pair of constants, so
            // judged clip by clip nothing ever moves. The span that
            // matters is between the extremes any state can reach.
            var span = new Dictionary<EditorCurveBinding, Vector2>();

            void Inspect(AnimationClip clip)
            {
                if (clip == null || !seen.Add(clip))
                {
                    return;
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(SkinnedMeshRenderer)
                        || !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve == null || curve.keys.Length == 0)
                    {
                        continue;
                    }
                    float low = curve.keys[0].value, high = low;
                    foreach (var key in curve.keys)
                    {
                        low = Mathf.Min(low, key.value);
                        high = Mathf.Max(high, key.value);
                    }
                    if (span.TryGetValue(binding, out var reach))
                    {
                        low = Mathf.Min(low, reach.x);
                        high = Mathf.Max(high, reach.y);
                    }
                    span[binding] = new Vector2(low, high);
                }
            }

            void Walk(Motion motion)
            {
                if (motion is AnimationClip clip)
                {
                    Inspect(clip);
                }
                else if (motion is BlendTree tree)
                {
                    foreach (var child in tree.children)
                    {
                        Walk(child.motion);
                    }
                }
            }

            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        Walk(child.state.motion);
                    }
                });
            }

            // Which bones a shape actually MOVES, from its own deltas. Matching on the renderer's
            // bone list instead flagged every chain on the avatar against every shape on the body:
            // a body mesh lists the whole armature, so a tongue shape came out "resizing" the arm
            // ribbons, and eighteen chains were named against forty shapes apiece. Deltas are the
            // difference between listing a bone and deforming around it.
            var movedCache = new Dictionary<string, HashSet<Transform>>(StringComparer.Ordinal);
            HashSet<Transform> BonesMovedBy(SkinnedMeshRenderer renderer, string shapeName, string key)
            {
                if (movedCache.TryGetValue(key, out var known))
                {
                    return known;
                }
                var moved = new HashSet<Transform>();
                movedCache[key] = moved;
                var mesh = renderer.sharedMesh;
                var bones = renderer.bones;
                if (mesh == null || bones == null)
                {
                    return moved;
                }
                int shape = mesh.GetBlendShapeIndex(shapeName);
                int frames = shape >= 0 ? mesh.GetBlendShapeFrameCount(shape) : 0;
                if (frames <= 0)
                {
                    return moved;
                }
                BoneWeight[] weights;
                Vector3[] deltas;
                try
                {
                    weights = mesh.boneWeights;
                    deltas = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(shape, frames - 1, deltas, null, null);
                }
                catch
                {
                    return moved;
                }
                if (weights.Length != deltas.Length)
                {
                    return moved;
                }
                for (int i = 0; i < deltas.Length; i++)
                {
                    // A millimetre. Below that the shape is touching this vertex incidentally.
                    if (deltas[i].sqrMagnitude < 1e-6f)
                    {
                        continue;
                    }
                    var w = weights[i];
                    if (w.weight0 >= 0.2f && w.boneIndex0 < bones.Length && bones[w.boneIndex0] != null) moved.Add(bones[w.boneIndex0]);
                    if (w.weight1 >= 0.2f && w.boneIndex1 < bones.Length && bones[w.boneIndex1] != null) moved.Add(bones[w.boneIndex1]);
                    if (w.weight2 >= 0.2f && w.boneIndex2 < bones.Length && bones[w.boneIndex2] != null) moved.Add(bones[w.boneIndex2]);
                    if (w.weight3 >= 0.2f && w.boneIndex3 < bones.Length && bones[w.boneIndex3] != null) moved.Add(bones[w.boneIndex3]);
                }
                return moved;
            }

            foreach (var pair in span)
            {
                // A shape every state pins to the same number resizes nothing.
                if (pair.Value.y - pair.Value.x < 1f)
                {
                    continue;
                }
                var binding = pair.Key;
                var target = string.IsNullOrEmpty(binding.path) ? root : root.Find(binding.path);
                var renderer = target != null ? target.GetComponent<SkinnedMeshRenderer>() : null;
                if (renderer == null || renderer.bones == null)
                {
                    continue;
                }
                string shapeName = binding.propertyName.Substring("blendShape.".Length);
                var moved = BonesMovedBy(renderer, shapeName, binding.path + "|" + shapeName);
                if (moved.Count == 0)
                {
                    continue;
                }
                foreach (var chain in chains)
                {
                    var chainRoot = chain.Root != null ? chain.Root
                        : chain.Source != null ? chain.Source.transform : null;
                    if (chainRoot == null)
                    {
                        continue;
                    }
                    foreach (var bone in moved)
                    {
                        if (bone != chainRoot && !bone.IsChildOf(chainRoot))
                        {
                            continue;
                        }
                        if (!driven.TryGetValue(chainRoot.name, out var shapes))
                        {
                            driven[chainRoot.name] = shapes = new SortedSet<string>(StringComparer.Ordinal);
                        }
                        shapes.Add(shapeName);
                        break;
                    }
                }
            }

            if (driven.Count == 0)
            {
                return;
            }
            // Four names is enough to recognise the slider; a chain deformed by twenty shapes
            // does not become clearer by listing all twenty.
            var lines = driven.Select(entry =>
            {
                var shown = entry.Value.Take(4).ToList();
                string extra = entry.Value.Count > shown.Count
                    ? $", +{entry.Value.Count - shown.Count} more" : "";
                return $"\"{entry.Key}\" ({string.Join(", ", shown)}{extra})";
            }).ToList();
            // Approximated rather than a Warning: nothing is broken, the size is simply fixed
            // where a slider is not. Calling it a warning puts thirteen alarms on an avatar that
            // converted correctly.
            ctx.Report.Approximated("PhysBones -> MagicaCloth2",
                $"{driven.Count} chain(s) are resized by a blendshape, so their collision size " +
                "cannot follow the slider",
                string.Join("; ", lines) + " — each of these chains has its collision sized from " +
                "the mesh as this avatar is saved, which is the right size at the slider position " +
                "it was saved at and too small or too large everywhere else. Sliders that work by " +
                "SCALING BONES do not have this problem: the cloth simulates those bones, so it " +
                "follows for free. A pure blendshape moves no bones, and MagicaCloth2 will not " +
                "accept a radius driven by animation — of its parameters only pose ratio, gravity, " +
                "damping, inertia, wind and blend weight can be animated at all. If one of these " +
                "sliders is the one people will actually use, set it where you want it to be " +
                "correct BEFORE converting, and convert again.");
        }

        // VRChat gates reaction layers by weight: the layer rests at 0,
        // and a VRCAnimatorLayerControl on a state raises it when the
        // reaction fires — headpats, boops, pulls are all built this
        // way. ChilloutVR has no layer-weight control, so those layers
        // converted as permanently invisible: the contact fired, the
        // parameter flipped, the reaction played at weight zero.
        //
        // The gate is rebuilt without weights: the layer runs at 1, and
        // every state asserts the rest value of what the layer animates,
        // so outside the reaction it holds the avatar exactly where the
        // hidden layer left it. Only bindings no other layer animates
        // are asserted; contested ones keep their existing arbitration.
        // Crossfade durations have no equivalent and become instant.
        static void ReviveWeightGatedLayers(AnimatorController master, BridgeContext ctx)
        {
            var raisedNames = new HashSet<string>();
            foreach (var (key, goal) in CapturedLayerControls)
            {
                if (goal >= 0.5f && MergedLayerNames.TryGetValue(key, out string name))
                {
                    raisedNames.Add(name);
                }
            }
            if (raisedNames.Count == 0)
            {
                return;
            }

            // Bindings animated by more than one layer stay untouched;
            // asserting rest over them would override the other layer.
            var layers = master.layers;
            var animatedBy = new Dictionary<EditorCurveBinding, int>();
            var layerClips = new List<(List<AnimationClip> clips, List<AnimatorState> states)>();
            foreach (var layer in layers)
            {
                var clips = new List<AnimationClip>();
                var states = new List<AnimatorState>();
                var seenTrees = new HashSet<BlendTree>();
                void Collect(Motion motion)
                {
                    if (motion is AnimationClip clip)
                    {
                        clips.Add(clip);
                    }
                    else if (motion is BlendTree tree && seenTrees.Add(tree))
                    {
                        foreach (var child in tree.children)
                        {
                            Collect(child.motion);
                        }
                    }
                }
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        if (child.state == null)
                        {
                            continue;
                        }
                        states.Add(child.state);
                        Collect(child.state.motion);
                    }
                });
                layerClips.Add((clips, states));
            }
            for (int i = 0; i < layers.Length; i++)
            {
                foreach (var clip in layerClips[i].clips.Distinct())
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        animatedBy[binding] = animatedBy.TryGetValue(binding, out int owner) && owner != i
                            ? -1 : i;   // -1 marks contested
                    }
                }
            }

            var revived = new List<string>();
            int curvesAdded = 0;
            int contested = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (!raisedNames.Contains(layer.name) || layer.defaultWeight > 0.5f)
                {
                    continue;
                }

                var mine = new List<EditorCurveBinding>();
                foreach (var clip in layerClips[i].clips.Distinct())
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type == typeof(Animator))
                        {
                            continue;   // parameters, not properties
                        }
                        if (animatedBy.TryGetValue(binding, out int owner) && owner == i)
                        {
                            if (!mine.Contains(binding))
                            {
                                mine.Add(binding);
                            }
                        }
                        else
                        {
                            contested++;
                        }
                    }
                }

                int added = 0;
                var perLayer = new Dictionary<AnimationClip, AnimationClip>();
                foreach (var state in layerClips[i].states)
                {
                    // Trees blend their gaps against defaults; only plain
                    // and empty states need the assertion written in.
                    if (state.motion is BlendTree)
                    {
                        continue;
                    }
                    var clip = state.motion as AnimationClip;
                    var drives = clip != null
                        ? new HashSet<EditorCurveBinding>(AnimationUtility.GetCurveBindings(clip))
                        : new HashSet<EditorCurveBinding>();
                    AnimationClip target = null;
                    foreach (var binding in mine)
                    {
                        if (drives.Contains(binding))
                        {
                            continue;
                        }
                        if (!AnimationUtility.GetFloatValue(ctx.Target, binding, out float rest))
                        {
                            continue;   // not present on this avatar
                        }
                        if (target == null)
                        {
                            if (clip == null)
                            {
                                target = new AnimationClip { name = SanitizeFileName($"{layer.name} rest") };
                                AssetDatabase.AddObjectToAsset(target, master);
                            }
                            else if (!perLayer.TryGetValue(clip, out target))
                            {
                                // Cloned per layer: the clip may sit in
                                // other layers that must not gain asserts.
                                target = UnityEngine.Object.Instantiate(clip);
                                target.name = clip.name;
                                target.hideFlags = HideFlags.None;
                                AssetDatabase.AddObjectToAsset(target, master);
                                perLayer[clip] = target;
                            }
                        }
                        AnimationUtility.SetEditorCurve(target, binding,
                            AnimationCurve.Constant(0f, 1f / 60f, rest));
                        added++;
                    }
                    if (target != null)
                    {
                        state.motion = target;
                    }
                }

                layer.defaultWeight = 1f;
                revived.Add($"\"{layer.name}\"");
                curvesAdded += added;
            }
            master.layers = layers;

            if (revived.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{revived.Count} weight-gated reaction layer(s) rebuilt without the weight",
                    string.Join(", ", revived) + $" — {curvesAdded} rest value(s) asserted. VRChat " +
                    "held these layers at weight 0 and raised them from a state behaviour when " +
                    "their contact or control fired; ChilloutVR cannot change layer weights, so " +
                    "converted as-is the reaction played invisibly — a booped nose that detected " +
                    "the touch and showed nothing. Each layer now runs at full weight and its " +
                    "states assert the avatar's resting values for everything the layer animates, " +
                    "so it is inert until the reaction actually plays. Fade-in durations from the " +
                    "removed behaviours have no equivalent and become instant." +
                    (contested > 0 ? $" {contested} propert(ies) shared with other layers were " +
                    "left to those layers' own handling." : ""));
            }
        }

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
            int curvesAdded = 0, clipsTouched = 0, deactivationsMirrored = 0, offsAsserted = 0;
            // Which cloth bindings each rewired clip switches on, and
            // which may be asserted off elsewhere. A target is off-safe
            // only while every activating container passed the
            // shared-rider test.
            var activatedByClip = new Dictionary<AnimationClip, HashSet<EditorCurveBinding>>();
            var offSafe = new Dictionary<EditorCurveBinding, bool>();
            var physicslessStyles = new HashSet<Transform>();
            // Chains left running when their style hides, because a mesh outside the toggled
            // object rides them. Named in the report so "this cloth never stops" is explained
            // rather than mysterious.
            var sharedChains = new SortedSet<string>(StringComparer.Ordinal);

            // PhysBone on/off curves with nowhere to land: the chain
            // produced no physics, so there is no component to retarget.
            // The toggle then does nothing, silently, while every other
            // part of it converts. Reported with the skip that caused it.
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

            // Everything that depends on a chain's simulated bones:
            // meshes weighted to them, and anything parented beneath.
            // Built once per chain.
            var ridersByChain = new Dictionary<BridgeContext.ConvertedPhysicsChain, List<Transform>>();
            List<Transform> RidersOf(BridgeContext.ConvertedPhysicsChain chain)
            {
                if (ridersByChain.TryGetValue(chain, out var known))
                {
                    return known;
                }
                var riders = new List<Transform>();
                var chainRoot = chain.Root != null ? chain.Root
                    : chain.Source != null ? chain.Source.transform : null;
                if (chainRoot != null)
                {
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        bool rides = renderer.transform == chainRoot
                            || renderer.transform.IsChildOf(chainRoot);
                        if (!rides && renderer is SkinnedMeshRenderer skinned)
                        {
                            foreach (var bone in skinned.bones)
                            {
                                if (bone != null && (bone == chainRoot || bone.IsChildOf(chainRoot)))
                                {
                                    rides = true;
                                    break;
                                }
                            }
                        }
                        if (rides)
                        {
                            riders.Add(renderer.transform);
                        }
                    }
                }
                ridersByChain[chain] = riders;
                return riders;
            }

            // True when a mesh outside the toggled object rides this
            // chain. Something still visible needs those bones moving,
            // so the cloth must not stop with the object.
            bool ChainSharedOutside(BridgeContext.ConvertedPhysicsChain chain, Transform animated)
            {
                foreach (var rider in RidersOf(chain))
                {
                    if (rider != animated && !rider.IsChildOf(animated))
                    {
                        return true;
                    }
                }
                return false;
            }

            // A self-contained rig: most of the container's skinned-mesh
            // bones live inside it. Clothing skinned to body bones does
            // not qualify. The rig root is the deepest common ancestor.
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

            // A toggled container with its own rig and no converted
            // chain never had physics in the source either. Reported,
            // so "broken" reads as "always rigid".
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
                            // Onto the component, not the holder's active
                            // flag. Holders are created active, with the
                            // cloth carrying the off state on its own
                            // enabled flag. An activation on m_IsActive
                            // would change nothing.
                            target = chain.Physics != null
                                ? EditorCurveBinding.FloatCurve(
                                    AnimationUtility.CalculateTransformPath(host, root),
                                    chain.Physics.GetType(), "m_Enabled")
                                : EditorCurveBinding.FloatCurve(
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
                            // A deactivation, mirrored rather than dropped.
                            // CVR never restores Write Defaults, so a cloth
                            // switched on would otherwise run forever.
                            //
                            // Switching the PhysBone's own object stops
                            // that one chain deliberately. Only a container
                            // deactivation must prove nothing still
                            // visible rides these bones.
                            if (source != animated && ChainSharedOutside(chain, animated))
                            {
                                sharedChains.Add(chain.Source.name);
                                offSafe[target] = false;
                                continue;
                            }
                            deactivationsMirrored++;
                        }
                        else
                        {
                            // An activation. Recorded rather than acted
                            // on; the off-asserting states are not known
                            // until every clip has been through this.
                            // Both kinds of toggle count. A component
                            // curve only matches its own chain and is
                            // always safe to stop.
                            bool safe = source == animated || !ChainSharedOutside(chain, animated);
                            if (!safe)
                            {
                                sharedChains.Add(chain.Source.name);
                            }
                            offSafe[target] = offSafe.TryGetValue(target, out bool had)
                                ? had && safe
                                : safe;
                        }
                        if (additions == null)
                        {
                            additions = new Dictionary<EditorCurveBinding, AnimationCurve>();
                        }
                        additions[target] = curve;
                    }

                    // Nothing to retarget at; this curve dies with the
                    // VRC components. Report which clip and which object.
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
                foreach (var pair in additions)
                {
                    if (!CurveActivates(pair.Value))
                    {
                        continue;
                    }
                    if (!activatedByClip.TryGetValue(clone, out var set))
                    {
                        activatedByClip[clone] = set = new HashSet<EditorCurveBinding>();
                    }
                    set.Add(pair.Key);
                }
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
            // Phase 1, before any curve is copied: "Add physics to
            // toggled rigs that have none". A toggled self-contained rig
            // with no converted chain gets a synthesized MagicaCloth.
            // Done here because only the animator knows what is toggled.
            // The new chain registers itself for phase 2 wiring.
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

            // Bindings the finished controller ALREADY switches off somewhere. A synthetic stop is
            // for a binding nothing ever takes back; where the avatar's own animation takes it back
            // already, adding one is at best redundant and at worst destructive.
            //
            // Paired chains exist: one enabled while the other disables,
            // swapped back by a defaults clip. A synthetic stop on top of
            // that leaves both disabled.
            var alreadySwitchedOff = new HashSet<EditorCurveBinding>();
            foreach (var pair in rewired)
            {
                var clip = pair.Value;
                if (clip == null) continue;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!offSafe.ContainsKey(binding)) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && !CurveActivates(curve))
                    {
                        alreadySwitchedOff.Add(binding);
                    }
                }
            }

            // The restore passes have already run, so the m_Enabled
            // bindings created above were invisible to them. Nothing
            // ever takes the on curve back, and CVR restores nothing.
            // A toggle with an empty off state has nothing to mirror.
            //
            // Scoped tightly: only bindings this pass switched on, only
            // in a layer that switches them, only into plain-clip states,
            // and only where the shared-rider guard allows a stop.
            foreach (var layer in master.layers)
            {
                var activatedHere = new HashSet<EditorCurveBinding>();
                var states = new List<AnimatorState>();
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        states.Add(child.state);
                        if (child.state.motion is AnimationClip clip
                            && activatedByClip.TryGetValue(clip, out var activated))
                        {
                            activatedHere.UnionWith(activated);
                        }
                    }
                });
                activatedHere.RemoveWhere(b => !offSafe.TryGetValue(b, out bool ok) || !ok
                                               || alreadySwitchedOff.Contains(b));
                // Two-state layers only, like the empty-state filler.
                // On a bigger machine the other states are unrelated,
                // not the off half of a toggle.
                if (activatedHere.Count == 0 || states.Count != 2)
                {
                    continue;
                }

                // Cloned per layer, never shared: the same clip can be the off state of this
                // toggle and of an unrelated layer, and a stop written into the shared asset
                // would have that other layer switching off physics it knows nothing about.
                var perLayer = new Dictionary<AnimationClip, AnimationClip>();
                foreach (var state in states)
                {
                    // A blend tree is left alone. A constant stop in a
                    // blended child fights the tree instead of resting it.
                    if (!(state.motion is AnimationClip clip))
                    {
                        continue;
                    }
                    var drives = new HashSet<EditorCurveBinding>(AnimationUtility.GetCurveBindings(clip));
                    foreach (var target in activatedHere)
                    {
                        if (drives.Contains(target))
                        {
                            continue;   // this state says its own piece already
                        }
                        if (!perLayer.TryGetValue(clip, out var owned))
                        {
                            owned = UnityEngine.Object.Instantiate(clip);
                            owned.name = clip.name;
                            owned.hideFlags = HideFlags.None;
                            perLayer[clip] = owned;
                        }
                        AnimationUtility.SetEditorCurve(owned, target, AnimationCurve.Constant(0f, 1f / 60f, 0f));
                        offsAsserted++;
                    }
                    if (perLayer.TryGetValue(clip, out var replacement))
                    {
                        state.motion = replacement;
                    }
                }
            }

            if (clipsTouched > 0)
            {
                ctx.Report.Converted(Category,
                    $"{curvesAdded} toggle curve(s) re-wired to generated physics in {clipsTouched} clip(s)",
                    "Animations that activated a converted PhysBone's object or component (hair swaps, " +
                    "outfit toggles) now activate the generated physics too. Without this, a chain " +
                    "belonging to a style that was inactive at conversion time could never wake up — " +
                    "its cloth lives on its own object at the avatar root, on a path the original " +
                    "animations never animated. " +
                    (deactivationsMirrored > 0
                        ? $"{deactivationsMirrored} of those curve(s) switch the physics back OFF " +
                          "again when the style hides, which matters here because ChilloutVR does " +
                          "not restore a binding nothing writes: without the off curve the cloth " +
                          "would stay running from the first time it appeared."
                        : "No style here switches its physics back off.") +
                    (offsAsserted > 0
                        ? $" A further {offsAsserted} resting state(s) were given an explicit stop " +
                          "for physics they leave alone. Those states were empty of it — VRChat " +
                          "relied on Write Defaults to undo the switch, and ChilloutVR has no such " +
                          "rule, so the cloth latched on the first time the toggle was used and " +
                          "never stopped. Chains that something outside the toggled object rides " +
                          "are excluded and listed separately."
                        : ""));
            }

            if (sharedChains.Count > 0)
            {
                ctx.Report.Converted(Category,
                    $"{sharedChains.Count} chain(s) keep simulating while their style is hidden",
                    string.Join(", ", sharedChains) + " — each of these is switched ON with the " +
                    "object it belongs to but never switched off, because a mesh OUTSIDE that " +
                    "object is skinned to the same bones. Add-on hair grafted onto a base " +
                    "hairstyle's rig is the usual shape. Stopping the chain with the base style's " +
                    "mesh would leave the add-on rigid, so it is left running instead: it " +
                    "simulates bones nobody can see, which costs a little performance and looks " +
                    "like nothing at all.");
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

        static void AuditClipBindings(AnimatorController master, BridgeContext ctx)
        {
            var root = ctx.Target.transform;

            // The source hierarchy, so a dead path can be blamed
            // correctly. Missing in the source too: already silent
            // before conversion. Present there but not here: a real
            // defect, reported as one.
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
            // Clips whose dead paths resolved before conversion.
            // Those breaks belong to this tool.
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
            // Not a warning. These paths were already missing on the
            // source avatar, so the curves were silent in VRChat too.
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
                            // Poiyomi records "animate this" as an override
                            // tag. Present here, the usual advice was
                            // already followed and still failed; do not
                            // repeat it.
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
                // Full PPtr syntax only: {fileID: N, guid: X, type: N}.
                // A bare "guid:" grep matches guid-looking text inside
                // string fields, like missing-prefab object names.
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

        internal static void NameGraftedEmoteClips(BridgeContext ctx)
        {
            var master = ctx.MergedController;
            if (master == null)
            {
                return;
            }
            var layer = master.layers.FirstOrDefault(l => l != null && l.name == "Locomotion/Emotes");
            if (layer?.stateMachine == null)
            {
                return;
            }
            // Copied, never renamed in place. A rename reaches every
            // reference the controller holds; the same clip can sit in
            // a grafted state and a puppet tree at once. A copy gives
            // just the grafted state its name and leaves every other
            // reference exactly as it was. One copy per source clip, shared by the states that
            // shared the original.
            var copies = new Dictionary<AnimationClip, AnimationClip>();
            var renamed = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var child in layer.stateMachine.states)
            {
                var state = child.state;
                if (state == null || state.name == null
                    || !state.name.StartsWith("[AB] ", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!(state.motion is AnimationClip clip) || clip == null
                    || clip.name.Contains("Emote"))
                {
                    continue;
                }
                if (!copies.TryGetValue(clip, out var copy))
                {
                    copy = UnityEngine.Object.Instantiate(clip);
                    copy.name = clip.name + " Emote";
                    string target = OutputAssetPaths.Claim(
                        $"{ctx.OutputDir}/RehomedAssets/{SanitizeFileName(copy.name)}.anim");
                    var folder = System.IO.Path.GetDirectoryName(target).Replace('\\', '/');
                    if (!AssetDatabase.IsValidFolder(folder))
                    {
                        System.IO.Directory.CreateDirectory(folder);
                        AssetDatabase.Refresh();
                    }
                    AssetDatabase.CreateAsset(copy, target);
                    copies[clip] = copy;
                }
                state.motion = copy;
                EditorUtility.SetDirty(state);
                renamed.Add($"\"{state.name.Substring(5)}\"");
            }
            if (renamed.Count == 0)
            {
                return;
            }
            ctx.Report.Converted(Category,
                $"{renamed.Count} emote(s) will free your hands while they play",
                string.Join(", ", renamed) + " — ChilloutVR decides whether something is an emote " +
                "by reading the playing clip's NAME, and mutes both hand-pose layers while one is " +
                "on. Converted emotes were named after whatever they were called in VRChat, so the " +
                "client never recognised them and your controller's gesture kept overriding the " +
                "pose the emote wanted. Their clips are named so it does. This is the hand half " +
                "only: ChilloutVR still has no finger mask for tracking control, so an emote " +
                "cannot pose individual fingers the way VRChat's could.");
        }

        internal static void StripDeadMaterialCurves(BridgeContext ctx)
        {
            var master = ctx.MergedController;
            if (master == null || !ctx.Settings.stripDeadMaterialAnimation)
            {
                return;
            }
            var root = ctx.Target.transform;

            // Renderers with animated material slots are off limits.
            // "No such property" is only true of this instant; the
            // swapped-in material may well have it.
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

            // Dropping the FT layers is not enough. VRCFury's Defaults
            // layer writes every parameter from one Direct tree, keeping
            // FT parameters "referenced". Prune them out of Direct trees
            // first, like GoGo and SPS.
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

        static AvatarMask GetNoMuscleMask(BridgeContext ctx)
        {
            return _noMuscleMask != null ? _noMuscleMask
                : _noMuscleMask = BuildRigMask("AvatarBridge_NoMuscles", ctx);
        }

        static AvatarMask GetFingersOnlyMask(BridgeContext ctx)
        {
            return _fingersOnlyMask != null ? _fingersOnlyMask
                : _fingersOnlyMask = BuildRigMask("AvatarBridge_FingersOnly", ctx,
                    AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers);
        }

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
