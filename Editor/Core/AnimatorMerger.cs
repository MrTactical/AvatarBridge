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
    ///  - GestureLeftWeight/RightWeight       -> GestureLeft/Right (CVR fist is analog)
    ///  - VRC parameter names                 -> CVR core names (Viseme -> VisemeIdx, ...)
    ///  - non-synced parameters               -> "#" prefix (CVR local-only convention)
    ///  - menu Buttons                        -> "&lt;impulse=0.1&gt;" auto-reset parameters
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

        // CVR drives these to non-zero at runtime; matching defaults avoids startup glitches.
        static readonly Dictionary<string, float> NonZeroDefaults = new Dictionary<string, float>
        {
            { "Grounded", 1f },
            { "ScaleFactor", 1f },
            { "ScaleFactorInverse", 1f },
            { "EyeHeightAsPercent", 1f }
        };

        // VRC built-ins with no CVR equivalent; they stay as frozen local parameters.
        // (MuteSelf/VRMode/GestureWeights are handled via CVRParameterStream instead.)
        static readonly HashSet<string> KnownUnsupportedVrcParameters = new HashSet<string>
        {
            "TrackingType", "Earmuffs", "Upright", "AngularY", "AFK",
            "AvatarVersion", "VelocityMagnitude", "GroundProximity", "InStation",
            "ScaleModified", "ScaleFactor", "ScaleFactorInverse", "EyeHeightAsMeters",
            "EyeHeightAsPercent", "IsAnimatorEnabled"
        };

        public static void Run(BridgeContext ctx)
        {
            var vrcControllers = GetSelectedVrcControllers(ctx);
            bool convertingGestureLayer = vrcControllers.Any(c => c.id == VRCAvatarDescriptor.AnimLayerType.Gesture);

            AnimatorController master = LoadBaseController(ctx, convertingGestureLayer);
            var masterLayers = master.layers.ToList();
            var vrcLayers = new List<AnimatorControllerLayer>();

            foreach (var (id, controller) in vrcControllers)
            {
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
                    if (firstLayerOfController)
                    {
                        // Unity forces a controller's first layer to weight 1; once merged it
                        // is no longer first, so bake that weight in.
                        clone.defaultWeight = 1f;
                        firstLayerOfController = false;
                    }
                    clone.avatarMask = ReplaceVrcMask(clone.avatarMask, ctx);
                    masterLayers.Add(clone);
                    vrcLayers.Add(clone);
                }
                ctx.Report.Converted(Category, $"{id} layer merged", $"{controller.layers.Length} sub-layers");
            }

            master.layers = masterLayers.ToArray();

            GesturePass(master, vrcLayers, ctx);
            BehaviourPass(master, vrcLayers, ctx);
            SystemStripper.Run(ctx, master, vrcLayers);
            ToggleNativizer.Run(ctx, master, vrcLayers);
            BoolifyToggleParameters(master, ctx);
            RenamePass(master, vrcLayers, ctx);
            ApplyParameterDefaults(master, ctx);
            ReconcileAasInputTypes(master, ctx);
            CreateParameterStreams(master, ctx);
            RehomeVolatileAssets(master, vrcLayers, ctx);
            DeduplicateLayers(master, ctx);
            WarnLocomotionOverrides(vrcLayers, ctx);

            master.name = SanitizeFileName(ctx.Target.name) + "_CVR";
            ctx.MergedController = master;

            // Persist controller + override controller and hook both to the CVRAvatar.
            string controllerPath = $"{ctx.OutputDir}/{master.name}.controller";
            AnimatorAssetSaver.Save(master, controllerPath);

            // No override controller is generated: the CCK's "Override Controller" field is
            // an INPUT for a user's pre-existing overrides, and an empty one adds nothing.
            FileUtil.DeleteFileOrDirectory($"{ctx.OutputDir}/{master.name}_Overrides.overrideController");

            ctx.CvrAvatar.avatarSettings.baseController = master;

            var animator = ctx.TargetAnimator;
            if (animator != null)
            {
                animator.runtimeAnimatorController = master;
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
                master.parameters = new[]
                {
                    new AnimatorControllerParameter { name = "GestureLeft", type = AnimatorControllerParameterType.Float },
                    new AnimatorControllerParameter { name = "GestureRight", type = AnimatorControllerParameterType.Float },
                    new AnimatorControllerParameter { name = "MovementX", type = AnimatorControllerParameterType.Float },
                    new AnimatorControllerParameter { name = "MovementY", type = AnimatorControllerParameterType.Float },
                    new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Float, defaultFloat = 1f },
                    new AnimatorControllerParameter { name = "Emote", type = AnimatorControllerParameterType.Int },
                    new AnimatorControllerParameter { name = "CancelEmote", type = AnimatorControllerParameterType.Trigger },
                    new AnimatorControllerParameter { name = "Toggle", type = AnimatorControllerParameterType.Int }
                };
                return master;
            }

            // When the VRC Gesture layer takes over hand animation, CVR's own hand layers
            // must go or they fight for the finger muscles.
            string[] allowedLayers = convertingGestureLayer
                ? new[] { "Locomotion/Emotes" }
                : new[] { "Locomotion/Emotes", "LeftHand", "RightHand" };

            var copier = new AnimatorDeepCopier();
            master.parameters = source.parameters.Select(AnimatorDeepCopier.CloneParameter).ToArray();
            master.layers = source.layers
                .Where(l => allowedLayers.Contains(l.name))
                .Select(copier.CloneLayer)
                .ToArray();

            ctx.Report.Converted(Category, "CCK base animator",
                $"Kept layers: {string.Join(", ", master.layers.Select(l => l.name))}");
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
            // Which VRC gesture ints (0..7) satisfy the original integer condition?
            var matched = new List<int>();
            for (int g = 0; g <= 7; g++)
            {
                bool match;
                switch (condition.mode)
                {
                    case AnimatorConditionMode.Equals: match = g == (int)condition.threshold; break;
                    case AnimatorConditionMode.NotEqual: match = g != (int)condition.threshold; break;
                    case AnimatorConditionMode.Greater: match = g > condition.threshold; break;
                    case AnimatorConditionMode.Less: match = g < condition.threshold; break;
                    default: match = true; break;
                }
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
                // Never true; keep it never-true with an impossible range.
                return new List<List<AnimatorCondition>>
                {
                    new List<AnimatorCondition>
                    {
                        new AnimatorCondition { parameter = condition.parameter, mode = AnimatorConditionMode.Greater, threshold = float.MaxValue }
                    }
                };
            }

            // Convert matched gestures to CVR value intervals and merge adjacent ones.
            var intervals = matched
                .Select(g => GestureMap.CvrRangeForVrcGesture(g))
                .OrderBy(r => r.min)
                .ToList();
            var merged = new List<(float min, float max)>();
            foreach (var interval in intervals)
            {
                if (merged.Count > 0 && interval.min <= merged[merged.Count - 1].max + 0.01f)
                {
                    merged[merged.Count - 1] = (merged[merged.Count - 1].min,
                        Mathf.Max(merged[merged.Count - 1].max, interval.max));
                }
                else
                {
                    merged.Add(interval);
                }
            }

            return merged.Select(r => new List<AnimatorCondition>
            {
                new AnimatorCondition { parameter = condition.parameter, mode = AnimatorConditionMode.Greater, threshold = r.min },
                new AnimatorCondition { parameter = condition.parameter, mode = AnimatorConditionMode.Less, threshold = r.max }
            }).ToList();
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

            foreach (var layer in vrcLayers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    machine.behaviours = ConvertBehaviours(master, machine.behaviours, null, ctx, skippedBehaviourCounts);
                    foreach (var child in machine.states)
                    {
                        child.state.behaviours = ConvertBehaviours(master, child.state.behaviours, child.state, ctx, skippedBehaviourCounts);
                    }
                });
            }

            foreach (var pair in skippedBehaviourCounts)
            {
                ctx.Report.Skipped(Category, pair.Key, $"{pair.Value}x removed (no ChilloutVR equivalent).");
            }
        }

        static StateMachineBehaviour[] ConvertBehaviours(AnimatorController master, StateMachineBehaviour[] behaviours,
            AnimatorState state, BridgeContext ctx, Dictionary<string, int> skipped)
        {
            if (behaviours == null || behaviours.Length == 0)
            {
                return behaviours;
            }

            var result = new List<StateMachineBehaviour>();
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
                bool preserved = CvrCoreParameters.Contains(result) ||
                                 ctx.PreserveParameters.Contains(name) ||
                                 ctx.PreserveParameters.Contains(result) ||
                                 ctx.ContactParameters.Contains(name);
                if (ctx.Settings.preserveParameterSyncState && !preserved)
                {
                    result = "#" + result;
                }
                if (ctx.ImpulseParameters.Contains(name))
                {
                    result += "<impulse=0.1>";
                }
                return result;
            }

            // Parameters (dedupe after rename; e.g. GestureLeftWeight folds into GestureLeft).
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
            foreach (var layer in vrcLayers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        state.timeParameter = Rename(state.timeParameter);
                        state.speedParameter = Rename(state.speedParameter);
                        state.mirrorParameter = Rename(state.mirrorParameter);
                        state.cycleOffsetParameter = Rename(state.cycleOffsetParameter);
                        state.motion = RenameInMotion(state.motion, Rename, clipMap, ctx);

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
            Dictionary<AnimationClip, AnimationClip> clipMap, BridgeContext ctx)
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
                    children[i].motion = RenameInMotion(children[i].motion, rename, clipMap, ctx);
                }
                tree.children = children;
                return tree;
            }

            if (motion is AnimationClip clip)
            {
                return RenameInClip(clip, rename, clipMap);
            }
            return motion;
        }

        /// <summary>
        /// Clips can animate animator parameters directly (AAPs). Those bindings live on
        /// the shared clip asset, so a renamed parameter forces a clone-on-write copy.
        /// </summary>
        static AnimationClip RenameInClip(AnimationClip clip, Func<string, string> rename,
            Dictionary<AnimationClip, AnimationClip> clipMap)
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
                if (KnownUnsupportedVrcParameters.Contains(bareName))
                {
                    unsupportedPresent.Add(bareName);
                }
            }
            master.parameters = parameters;

            if (unsupportedPresent.Count > 0)
            {
                ctx.Report.Skipped(Category, "VRC built-in parameters without CVR equivalent",
                    string.Join(", ", unsupportedPresent.Distinct()) + " — they keep their default value.");
            }
        }

        /// <summary>
        /// VRCFury bakes toggles as float parameters. When a toggle parameter is used
        /// ONLY in transition conditions (typical after nativizing/expanding toggles),
        /// it can safely become a real Bool — cleaner menu, cleaner sync, no 0.5 checks.
        /// Parameters read by blend trees, motion time, drivers or written by AAP clips
        /// must stay float.
        /// </summary>
        static void BoolifyToggleParameters(AnimatorController master, BridgeContext ctx)
        {
            var toggleParams = new HashSet<string>(ctx.CvrAvatar.avatarSettings.settings
                .Where(e => e.setting is ABI.CCK.Scripts.CVRAdvancesAvatarSettingGameObjectToggle &&
                            !string.IsNullOrEmpty(e.machineName))
                .Select(e => e.machineName));
            if (toggleParams.Count == 0)
            {
                return;
            }

            // Everything that reads/writes parameters outside of transition conditions.
            var nonConditionRefs = new HashSet<string>();
            void NoteMotion(Motion motion)
            {
                if (motion is BlendTree tree)
                {
                    nonConditionRefs.Add(tree.blendParameter);
                    nonConditionRefs.Add(tree.blendParameterY);
                    foreach (var child in tree.children)
                    {
                        if (tree.blendType == BlendTreeType.Direct)
                        {
                            nonConditionRefs.Add(child.directBlendParameter);
                        }
                        NoteMotion(child.motion);
                    }
                }
                else if (motion is AnimationClip clip)
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path))
                        {
                            nonConditionRefs.Add(binding.propertyName); // AAP write
                        }
                    }
                }
            }
            foreach (var layer in master.layers)
            {
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var behaviour in machine.behaviours.OfType<AnimatorDriver>())
                    {
                        foreach (var task in behaviour.EnterTasks.Concat(behaviour.ExitTasks))
                        {
                            nonConditionRefs.Add(task.targetName);
                            nonConditionRefs.Add(task.aName);
                            nonConditionRefs.Add(task.bName);
                        }
                    }
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        if (state.timeParameterActive) nonConditionRefs.Add(state.timeParameter);
                        if (state.speedParameterActive) nonConditionRefs.Add(state.speedParameter);
                        if (state.mirrorParameterActive) nonConditionRefs.Add(state.mirrorParameter);
                        if (state.cycleOffsetParameterActive) nonConditionRefs.Add(state.cycleOffsetParameter);
                        NoteMotion(state.motion);
                        foreach (var behaviour in state.behaviours.OfType<AnimatorDriver>())
                        {
                            foreach (var task in behaviour.EnterTasks.Concat(behaviour.ExitTasks))
                            {
                                nonConditionRefs.Add(task.targetName);
                                nonConditionRefs.Add(task.aName);
                                nonConditionRefs.Add(task.bName);
                            }
                        }
                    }
                });
            }

            var boolified = new HashSet<string>();
            var parameters = master.parameters;
            foreach (var param in parameters)
            {
                if (param.type == AnimatorControllerParameterType.Float &&
                    toggleParams.Contains(param.name) &&
                    !nonConditionRefs.Contains(param.name))
                {
                    param.type = AnimatorControllerParameterType.Bool;
                    param.defaultBool = param.defaultFloat != 0f;
                    boolified.Add(param.name);
                }
            }
            if (boolified.Count == 0)
            {
                return;
            }
            master.parameters = parameters;

            // Rewrite every condition on the retyped parameters to bool comparisons.
            void RewriteBoolConditions(AnimatorTransitionBase[] transitions)
            {
                foreach (var transition in transitions)
                {
                    var conditions = transition.conditions;
                    bool changed = false;
                    for (int i = 0; i < conditions.Length; i++)
                    {
                        if (!boolified.Contains(conditions[i].parameter))
                        {
                            continue;
                        }
                        AnimatorConditionMode mode;
                        switch (conditions[i].mode)
                        {
                            case AnimatorConditionMode.Greater: mode = AnimatorConditionMode.If; break;
                            case AnimatorConditionMode.Less: mode = AnimatorConditionMode.IfNot; break;
                            case AnimatorConditionMode.Equals:
                                mode = conditions[i].threshold != 0f ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                                break;
                            case AnimatorConditionMode.NotEqual:
                                mode = conditions[i].threshold != 0f ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If;
                                break;
                            default: mode = conditions[i].mode; break;
                        }
                        conditions[i].mode = mode;
                        conditions[i].threshold = 0f;
                        changed = true;
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
                    RewriteBoolConditions(machine.anyStateTransitions);
                    RewriteBoolConditions(machine.entryTransitions);
                    foreach (var child in machine.states)
                    {
                        RewriteBoolConditions(child.state.transitions);
                    }
                });
            }

            ctx.Report.Converted(Category, $"{boolified.Count} toggle parameter(s) retyped Float -> Bool",
                "They were only used in conditions; menu and sync now use real bools.");
        }

        /// <summary>
        /// CVR Parameter Streams feed values VRChat provided as built-in parameters:
        /// trigger squeeze (GestureLeft/RightWeight), mute state and VR mode. Entries are
        /// created via reflection because enum member layouts vary between CCK versions.
        /// </summary>
        static void CreateParameterStreams(AnimatorController master, BridgeContext ctx)
        {
            var streamables = new[]
            {
                (bare: "GestureLeftWeight", streamType: "TriggerLeftValue"),
                (bare: "GestureRightWeight", streamType: "TriggerRightValue"),
                (bare: "MuteSelf", streamType: "LocalPlayerMuted"),
                (bare: "VRMode", streamType: "DeviceMode")
            };
            var wanted = new List<(string paramName, string streamType, string bare)>();
            foreach (var param in master.parameters)
            {
                string bare = param.name.TrimStart('#');
                foreach (var s in streamables)
                {
                    if (bare == s.bare)
                    {
                        wanted.Add((param.name, s.streamType, s.bare));
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

            object ParseEnum(Type enumType, string name)
            {
                if (enumType == null)
                {
                    return null;
                }
                try { return Enum.Parse(enumType, name, true); }
                catch { return null; }
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
                var entry = Activator.CreateInstance(entryType);
                SetField(entry, "type", streamTypeValue);
                SetField(entry, "targetType", ParseEnum(targetEnum, "Animator"));
                SetField(entry, "applicationType", ParseEnum(appEnum, "Override"));
                SetField(entry, "parameterName", w.paramName);
                entries.Add(entry);
                ctx.Report.Converted(Category, $"\"{w.paramName}\" fed by CVR Parameter Stream ({w.streamType})",
                    "Behaves like the VRChat built-in parameter.");
            }
            EditorUtility.SetDirty(stream);
        }

        static string SanitizeParameterName(string source)
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
        /// VRCFury bakes its generated clips and masks into Packages/com.vrcfury.temp,
        /// which Fury DELETES on the next build — leaving every reference as "None".
        /// Copy anything volatile into our own controller so the output is self-contained.
        /// </summary>
        static void RehomeVolatileAssets(AnimatorController master, List<AnimatorControllerLayer> vrcLayers, BridgeContext ctx)
        {
            var clipMap = new Dictionary<AnimationClip, AnimationClip>();

            bool IsVolatile(UnityEngine.Object obj)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                return !string.IsNullOrEmpty(path) &&
                       path.Replace('\\', '/').StartsWith("Packages/com.vrcfury", StringComparison.OrdinalIgnoreCase);
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
                    clipMap[clip] = clone;
                }
                return clone;
            }

            Motion RehomeMotion(Motion motion)
            {
                if (motion is AnimationClip clip)
                {
                    return RehomeClip(clip);
                }
                if (motion is BlendTree tree)
                {
                    var children = tree.children;
                    for (int i = 0; i < children.Length; i++)
                    {
                        children[i].motion = RehomeMotion(children[i].motion);
                    }
                    tree.children = children;
                }
                return motion;
            }

            foreach (var layer in vrcLayers)
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
        static void WarnLocomotionOverrides(List<AnimatorControllerLayer> vrcLayers, BridgeContext ctx)
        {
            var bodyMuscleNames = new HashSet<string>(HumanTrait.MuscleName.Select(name =>
            {
                var match = System.Text.RegularExpressions.Regex.Match(name, @"^(Left|Right) (Thumb|Index|Middle|Ring|Little) (.*)$");
                return match.Success ? $"{match.Groups[1].Value}Hand.{match.Groups[2].Value}.{match.Groups[3].Value}" : name;
            }));
            bool IsFingerCurve(string property) => property.Contains("Hand.");
            bool IsRootCurve(string property) =>
                property.StartsWith("RootT") || property.StartsWith("RootQ") ||
                property.StartsWith("MotionT") || property.StartsWith("MotionQ");

            foreach (var layer in vrcLayers)
            {
                if (layer.name == "LeftHand" || layer.name == "RightHand")
                {
                    continue; // hand pose layers are supposed to animate finger muscles
                }
                bool animatesBody = false;
                WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        foreach (var clip in CollectClips(child.state.motion))
                        {
                            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                            {
                                if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path) &&
                                    (IsRootCurve(binding.propertyName) ||
                                     (bodyMuscleNames.Contains(binding.propertyName) && !IsFingerCurve(binding.propertyName))))
                                {
                                    animatesBody = true;
                                }
                            }
                        }
                    }
                });
                if (animatesBody)
                {
                    ctx.Report.Warning(Category, $"Layer \"{layer.name}\" animates body muscles or root motion",
                        "It can override CVR's locomotion/pose. Review it; lower its weight or delete it if movement breaks.");
                }
            }
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

        static AvatarMask _handLeftMask, _handRightMask, _handsOnlyMask, _musclesOnlyMask;

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
                    return _handsOnlyMask = _handsOnlyMask != null ? _handsOnlyMask
                        : BuildMask("AvatarBridge_HandsOnly", AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers);
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
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
#endif
