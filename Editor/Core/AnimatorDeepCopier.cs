using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// Deep-copies animator controller content so AvatarBridge never mutates the user's
    /// original VRChat controllers. Animation clips are shared by reference (they are not
    /// modified by the conversion except via clone-on-write in the rename pass).
    ///
    /// Usage: create one instance per source controller, call CloneLayer() for each layer.
    /// State/state-machine cross references (transition destinations) are resolved from
    /// the internal maps, so all layers of one controller must be cloned by one instance.
    /// </summary>
    public class AnimatorDeepCopier
    {
        readonly Dictionary<AnimatorState, AnimatorState> _stateMap = new Dictionary<AnimatorState, AnimatorState>();
        readonly Dictionary<AnimatorStateMachine, AnimatorStateMachine> _machineMap = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();

        public static AnimatorControllerParameter CloneParameter(AnimatorControllerParameter src)
        {
            return new AnimatorControllerParameter
            {
                name = src.name,
                type = src.type,
                defaultBool = src.defaultBool,
                defaultFloat = src.defaultFloat,
                defaultInt = src.defaultInt
            };
        }

        public AnimatorControllerLayer CloneLayer(AnimatorControllerLayer src)
        {
            var layer = new AnimatorControllerLayer
            {
                name = src.name,
                avatarMask = src.avatarMask,
                blendingMode = src.blendingMode,
                defaultWeight = src.defaultWeight,
                iKPass = src.iKPass,
                syncedLayerIndex = -1,
                syncedLayerAffectsTiming = src.syncedLayerAffectsTiming
            };

            if (src.stateMachine != null)
            {
                layer.stateMachine = CloneStateMachineStructure(src.stateMachine);
                ResolveTransitions(src.stateMachine);
            }
            return layer;
        }

        // Phase 1: clone machines, states, motions and behaviours (no transitions yet).
        AnimatorStateMachine CloneStateMachineStructure(AnimatorStateMachine src)
        {
            var dst = new AnimatorStateMachine
            {
                name = src.name,
                anyStatePosition = src.anyStatePosition,
                entryPosition = src.entryPosition,
                exitPosition = src.exitPosition,
                parentStateMachinePosition = src.parentStateMachinePosition,
                hideFlags = HideFlags.HideInHierarchy
            };
            _machineMap[src] = dst;

            dst.states = src.states.Select(child => new ChildAnimatorState
            {
                position = child.position,
                state = CloneState(child.state)
            }).ToArray();

            dst.stateMachines = src.stateMachines.Select(child => new ChildAnimatorStateMachine
            {
                position = child.position,
                stateMachine = CloneStateMachineStructure(child.stateMachine)
            }).ToArray();

            if (src.defaultState != null && _stateMap.TryGetValue(src.defaultState, out var mappedDefault))
            {
                dst.defaultState = mappedDefault;
            }

            dst.behaviours = CloneBehaviours(src.behaviours);
            return dst;
        }

        AnimatorState CloneState(AnimatorState src)
        {
            var dst = new AnimatorState
            {
                name = src.name,
                speed = src.speed,
                cycleOffset = src.cycleOffset,
                mirror = src.mirror,
                iKOnFeet = src.iKOnFeet,
                writeDefaultValues = src.writeDefaultValues,
                tag = src.tag,
                speedParameter = src.speedParameter,
                speedParameterActive = src.speedParameterActive,
                cycleOffsetParameter = src.cycleOffsetParameter,
                cycleOffsetParameterActive = src.cycleOffsetParameterActive,
                mirrorParameter = src.mirrorParameter,
                mirrorParameterActive = src.mirrorParameterActive,
                timeParameter = src.timeParameter,
                timeParameterActive = src.timeParameterActive,
                motion = CloneMotion(src.motion),
                hideFlags = HideFlags.HideInHierarchy
            };
            dst.behaviours = CloneBehaviours(src.behaviours);
            _stateMap[src] = dst;
            return dst;
        }

        StateMachineBehaviour[] CloneBehaviours(StateMachineBehaviour[] src)
        {
            if (src == null || src.Length == 0)
            {
                return new StateMachineBehaviour[0];
            }
            return src.Where(b => b != null).Select(b =>
            {
                var clone = Object.Instantiate(b);
                clone.name = b.name;
                clone.hideFlags = HideFlags.HideInHierarchy;
                return clone;
            }).ToArray();
        }

        public Motion CloneMotion(Motion src)
        {
            if (src is BlendTree tree)
            {
                var dst = new BlendTree
                {
                    name = tree.name,
                    blendType = tree.blendType,
                    blendParameter = tree.blendParameter,
                    blendParameterY = tree.blendParameterY,
                    minThreshold = tree.minThreshold,
                    maxThreshold = tree.maxThreshold,
                    hideFlags = HideFlags.HideInHierarchy
                };
                dst.children = tree.children.Select(child => new ChildMotion
                {
                    motion = CloneMotion(child.motion),
                    threshold = child.threshold,
                    position = child.position,
                    timeScale = child.timeScale,
                    cycleOffset = child.cycleOffset,
                    directBlendParameter = child.directBlendParameter,
                    mirror = child.mirror
                }).ToArray();
                dst.useAutomaticThresholds = tree.useAutomaticThresholds;
                return dst;
            }
            // Animation clips are shared, not cloned.
            return src;
        }

        // Phase 2: clone transitions now that every state/machine has a counterpart.
        void ResolveTransitions(AnimatorStateMachine src)
        {
            var dst = _machineMap[src];

            foreach (var child in src.states)
            {
                _stateMap[child.state].transitions =
                    child.state.transitions.Select(CloneStateTransition).ToArray();
            }

            dst.anyStateTransitions = src.anyStateTransitions.Select(CloneStateTransition).ToArray();
            dst.entryTransitions = src.entryTransitions.Select(CloneTransition).ToArray();

            foreach (var child in src.stateMachines)
            {
                // State-machine exit/entry transitions attached to the parent machine.
                var srcTransitions = src.GetStateMachineTransitions(child.stateMachine);
                if (srcTransitions != null && srcTransitions.Length > 0)
                {
                    dst.SetStateMachineTransitions(
                        _machineMap[child.stateMachine],
                        srcTransitions.Select(CloneTransition).ToArray());
                }
                ResolveTransitions(child.stateMachine);
            }
        }

        AnimatorStateTransition CloneStateTransition(AnimatorStateTransition src)
        {
            var dst = new AnimatorStateTransition
            {
                name = src.name,
                conditions = src.conditions.ToArray(),
                isExit = src.isExit,
                solo = src.solo,
                mute = src.mute,
                duration = src.duration,
                offset = src.offset,
                exitTime = src.exitTime,
                hasExitTime = src.hasExitTime,
                hasFixedDuration = src.hasFixedDuration,
                interruptionSource = src.interruptionSource,
                orderedInterruption = src.orderedInterruption,
                canTransitionToSelf = src.canTransitionToSelf,
                hideFlags = HideFlags.HideInHierarchy
            };
            MapDestination(src, dst);
            return dst;
        }

        AnimatorTransition CloneTransition(AnimatorTransition src)
        {
            var dst = new AnimatorTransition
            {
                name = src.name,
                conditions = src.conditions.ToArray(),
                isExit = src.isExit,
                solo = src.solo,
                mute = src.mute,
                hideFlags = HideFlags.HideInHierarchy
            };
            MapDestination(src, dst);
            return dst;
        }

        void MapDestination(AnimatorTransitionBase src, AnimatorTransitionBase dst)
        {
            if (src.destinationState != null && _stateMap.TryGetValue(src.destinationState, out var state))
            {
                dst.destinationState = state;
            }
            if (src.destinationStateMachine != null && _machineMap.TryGetValue(src.destinationStateMachine, out var machine))
            {
                dst.destinationStateMachine = machine;
            }
        }
    }
}
