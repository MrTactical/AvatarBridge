#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    /// <summary>
    /// Converts simple GameObject on/off toggles into NATIVE ChilloutVR menu toggles.
    ///
    /// VRCFury bakes each toggle as an FX layer (or blend tree) animating m_IsActive via
    /// a float parameter. In CVR the far better home is the CCK's own animator builder:
    /// a GameObjectToggle entry with explicit targets, generated at upload as a clean
    /// bool. This pass finds layers that do nothing but flip objects, moves their targets
    /// into the menu entry, deletes the layer and lets the parameter become a real bool.
    /// </summary>
    public static class ToggleNativizer
    {
        const string Category = "Native toggles";

        class TargetInfo
        {
            public string Path;
            public bool OnState;
        }

        static bool _targetsUnsupportedReported;

        public static void Run(BridgeContext ctx, AnimatorController master, List<AnimatorControllerLayer> vrcLayers)
        {
            if (!ctx.Settings.nativizeObjectToggles)
            {
                return;
            }
            _targetsUnsupportedReported = false;

            var entriesByParam = new Dictionary<string, CVRAdvancedSettingsEntry>();
            foreach (var entry in ctx.CvrAvatar.avatarSettings.settings)
            {
                if (entry.setting is CVRAdvancesAvatarSettingGameObjectToggle &&
                    !string.IsNullOrEmpty(entry.machineName) &&
                    !entriesByParam.ContainsKey(entry.machineName))
                {
                    entriesByParam.Add(entry.machineName, entry);
                }
            }
            if (entriesByParam.Count == 0)
            {
                return;
            }

            var removedMachines = new HashSet<AnimatorStateMachine>();
            var nativizedParams = new HashSet<string>();

            foreach (var layer in vrcLayers.ToList())
            {
                if (layer.stateMachine == null || layer.stateMachine.stateMachines.Length > 0)
                {
                    continue;
                }
                var refs = SystemStripper.CollectParameterRefs(layer.stateMachine);
                if (refs.Count != 1)
                {
                    continue;
                }
                string param = refs.First();
                if (!entriesByParam.TryGetValue(param, out var entry))
                {
                    continue;
                }

                var targets = AnalyzeToggleLayer(layer.stateMachine, param);
                if (targets == null || targets.Count == 0)
                {
                    continue;
                }
                if (!ApplyTargets(ctx, entry, targets))
                {
                    return; // CCK version without native targets; keep animator toggles
                }

                removedMachines.Add(layer.stateMachine);
                vrcLayers.Remove(layer);
                nativizedParams.Add(param);
                ctx.Report.Converted(Category, entry.name,
                    $"{targets.Count} object(s) toggled natively by CVR; layer \"{layer.name}\" removed.");
            }

            // Modern VRCFury merges many toggles as branches of shared direct blend
            // trees; pull pure object toggles out of those too.
            bool aborted = false;
            foreach (var layer in vrcLayers)
            {
                if (aborted)
                {
                    break;
                }
                string layerName = layer.name;
                SystemStripper.WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        if (!aborted && child.state.motion is BlendTree tree)
                        {
                            NativizeTreeToggles(ctx, tree, entriesByParam, nativizedParams, layerName, ref aborted);
                        }
                    }
                });
            }

            // Branches still keyed to toggle menu entries animate more than plain object
            // state (blendshapes, renderers, materials). Expand those into classic
            // hand-authored-style On/Off toggle layers instead of leaving them buried in
            // VRCFury's merged service tree.
            var expandedLayers = new List<AnimatorControllerLayer>();
            var expandedParams = new HashSet<string>();
            foreach (var layer in vrcLayers)
            {
                SystemStripper.WalkMachines(layer.stateMachine, machine =>
                {
                    foreach (var child in machine.states)
                    {
                        if (child.state.motion is BlendTree tree)
                        {
                            ExpandToggleBranches(ctx, master, tree, entriesByParam, expandedLayers, expandedParams);
                        }
                    }
                });
            }
            if (expandedLayers.Count > 0)
            {
                master.layers = master.layers.Concat(expandedLayers).ToArray();
                vrcLayers.AddRange(expandedLayers);
            }

            // Fury's merged-service layer name means nothing to humans; whatever internal
            // parameter math survives gets an honest label.
            var relabeled = master.layers;
            bool didRelabel = false;
            for (int i = 0; i < relabeled.Length; i++)
            {
                if (relabeled[i].name.Contains("LayerToTreeService"))
                {
                    relabeled[i].name = "[FX] Internal Parameter Math (VRCFury)";
                    didRelabel = true;
                }
            }
            if (didRelabel)
            {
                master.layers = relabeled;
            }

            if (removedMachines.Count == 0 && nativizedParams.Count == 0 && expandedLayers.Count == 0)
            {
                return;
            }

            if (removedMachines.Count > 0)
            {
                master.layers = master.layers
                    .Where(l => l.stateMachine == null || !removedMachines.Contains(l.stateMachine))
                    .ToArray();
            }

            // Parameters nothing references any more disappear from the controller, so the
            // CCK generates them fresh as real bools (checkbox in the menu AND inspector).
            var stillReferenced = new HashSet<string>();
            foreach (var layer in master.layers)
            {
                stillReferenced.UnionWith(SystemStripper.CollectParameterRefs(layer.stateMachine));
            }
            master.parameters = master.parameters
                .Where(p => !nativizedParams.Contains(p.name) || stillReferenced.Contains(p.name))
                .ToArray();
            foreach (var param in nativizedParams)
            {
                if (!stillReferenced.Contains(param) && entriesByParam[param].setting != null)
                {
                    entriesByParam[param].setting.usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool;
                }
            }
            ctx.Report.Converted(Category,
                $"{nativizedParams.Count} toggle(s) handed to CVR's own animator builder");
        }

        static void NativizeTreeToggles(BridgeContext ctx, BlendTree tree,
            Dictionary<string, CVRAdvancedSettingsEntry> entriesByParam,
            HashSet<string> nativizedParams, string layerName, ref bool aborted)
        {
            var children = tree.children;
            var kept = new List<ChildMotion>(children.Length);
            bool changed = false;
            foreach (var child in children)
            {
                if (!aborted &&
                    tree.blendType == BlendTreeType.Direct &&
                    !string.IsNullOrEmpty(child.directBlendParameter) &&
                    entriesByParam.TryGetValue(child.directBlendParameter, out var entry) &&
                    child.motion is AnimationClip clip)
                {
                    var targets = ExtractPureToggleTargets(clip);
                    if (targets != null && targets.Count > 0)
                    {
                        if (ApplyTargets(ctx, entry, targets))
                        {
                            nativizedParams.Add(child.directBlendParameter);
                            changed = true;
                            ctx.Report.Converted(Category, entry.name,
                                $"{targets.Count} object(s) toggled natively by CVR; branch removed from \"{layerName}\".");
                            continue; // drop this branch
                        }
                        aborted = true;
                    }
                }
                var keptChild = child;
                if (child.motion is BlendTree subTree)
                {
                    NativizeTreeToggles(ctx, subTree, entriesByParam, nativizedParams, layerName, ref aborted);
                }
                kept.Add(keptChild);
            }
            if (changed)
            {
                tree.children = kept.ToArray();
            }
        }

        static void ExpandToggleBranches(BridgeContext ctx, AnimatorController master, BlendTree tree,
            Dictionary<string, CVRAdvancedSettingsEntry> entriesByParam,
            List<AnimatorControllerLayer> expandedLayers, HashSet<string> expandedParams)
        {
            var children = tree.children;
            var kept = new List<ChildMotion>(children.Length);
            bool changed = false;
            foreach (var child in children)
            {
                if (tree.blendType == BlendTreeType.Direct &&
                    !string.IsNullOrEmpty(child.directBlendParameter) &&
                    entriesByParam.ContainsKey(child.directBlendParameter) &&
                    !expandedParams.Contains(child.directBlendParameter) &&
                    child.motion is AnimationClip clip)
                {
                    var entry = entriesByParam[child.directBlendParameter];
                    expandedLayers.Add(BuildToggleLayer(master, entry, child.directBlendParameter, clip));
                    expandedParams.Add(child.directBlendParameter);
                    changed = true;
                    ctx.Report.Converted(Category, entry.name,
                        "Expanded into a classic On/Off toggle layer (it animates more than object on/off).");
                    continue;
                }
                var keptChild = child;
                if (child.motion is BlendTree subTree)
                {
                    ExpandToggleBranches(ctx, master, subTree, entriesByParam, expandedLayers, expandedParams);
                }
                kept.Add(keptChild);
            }
            if (changed)
            {
                tree.children = kept.ToArray();
            }
        }

        /// <summary>A per-toggle layer exactly like a hand-authored Unity toggle.</summary>
        static AnimatorControllerLayer BuildToggleLayer(AnimatorController master,
            CVRAdvancedSettingsEntry entry, string parameter, AnimationClip clip)
        {
            bool isBoolParam = master.parameters
                .Any(p => p.name == parameter && p.type == AnimatorControllerParameterType.Bool);

            var off = new AnimatorState { name = "Off", writeDefaultValues = true, hideFlags = HideFlags.HideInHierarchy };
            var on = new AnimatorState { name = "On", motion = clip, writeDefaultValues = true, hideFlags = HideFlags.HideInHierarchy };

            var toOn = new AnimatorStateTransition
            {
                destinationState = on,
                hasExitTime = false,
                duration = 0f,
                hideFlags = HideFlags.HideInHierarchy,
                conditions = new[]
                {
                    new AnimatorCondition
                    {
                        parameter = parameter,
                        mode = isBoolParam ? AnimatorConditionMode.If : AnimatorConditionMode.Greater,
                        threshold = 0.5f
                    }
                }
            };
            var toOff = new AnimatorStateTransition
            {
                destinationState = off,
                hasExitTime = false,
                duration = 0f,
                hideFlags = HideFlags.HideInHierarchy,
                conditions = new[]
                {
                    new AnimatorCondition
                    {
                        parameter = parameter,
                        mode = isBoolParam ? AnimatorConditionMode.IfNot : AnimatorConditionMode.Less,
                        threshold = 0.5f
                    }
                }
            };
            off.transitions = new[] { toOn };
            on.transitions = new[] { toOff };

            var machine = new AnimatorStateMachine
            {
                name = "Toggle " + entry.name,
                hideFlags = HideFlags.HideInHierarchy,
                states = new[]
                {
                    new ChildAnimatorState { state = off, position = new Vector3(250, 0, 0) },
                    new ChildAnimatorState { state = on, position = new Vector3(250, 120, 0) }
                }
            };
            bool defaultOn = entry.setting is CVRAdvancesAvatarSettingGameObjectToggle toggle && toggle.defaultValue;
            machine.defaultState = defaultOn ? on : off;

            return new AnimatorControllerLayer
            {
                name = "Toggle " + entry.name,
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = machine
            };
        }

        /// <summary>Targets of a clip that ONLY flips GameObjects on/off; null otherwise.</summary>
        static List<TargetInfo> ExtractPureToggleTargets(AnimationClip clip)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Length == 0)
            {
                return null;
            }
            var targets = new List<TargetInfo>();
            foreach (var binding in bindings)
            {
                if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive")
                {
                    return null;
                }
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null && curve.length > 0)
                {
                    targets.Add(new TargetInfo
                    {
                        Path = binding.path,
                        OnState = curve.keys[curve.length - 1].value > 0.5f
                    });
                }
            }
            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0)
            {
                return null;
            }
            return targets;
        }

        // ---------------------------------------------------------------- analysis ----

        /// <summary>
        /// Returns the objects this layer toggles, or null when the layer does anything
        /// beyond flipping GameObjects on/off (then it must stay an animator layer).
        /// </summary>
        static List<TargetInfo> AnalyzeToggleLayer(AnimatorStateMachine machine, string param)
        {
            var states = machine.states.Select(c => c.state).ToArray();
            if (states.Length == 0)
            {
                return null;
            }

            // Classify states as ON/OFF from the transition conditions that lead to them.
            var onness = new Dictionary<AnimatorState, bool>();
            void Classify(AnimatorTransitionBase t)
            {
                if (t.destinationState == null)
                {
                    return;
                }
                foreach (var c in t.conditions)
                {
                    if (c.parameter != param)
                    {
                        continue;
                    }
                    switch (c.mode)
                    {
                        case AnimatorConditionMode.If: onness[t.destinationState] = true; break;
                        case AnimatorConditionMode.Greater: onness[t.destinationState] = c.threshold >= 0f; break;
                        case AnimatorConditionMode.IfNot: onness[t.destinationState] = false; break;
                        case AnimatorConditionMode.Less: onness[t.destinationState] = false; break;
                        case AnimatorConditionMode.Equals: onness[t.destinationState] = c.threshold != 0f; break;
                        case AnimatorConditionMode.NotEqual: onness[t.destinationState] = c.threshold == 0f; break;
                    }
                }
            }
            foreach (var t in machine.anyStateTransitions) Classify(t);
            foreach (var t in machine.entryTransitions) Classify(t);
            foreach (var s in states)
                foreach (var t in s.transitions) Classify(t);

            var onValues = new Dictionary<string, bool>();
            var offValues = new Dictionary<string, bool>();

            bool Extract(AnimationClip clip, Dictionary<string, bool> into)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive")
                    {
                        return false; // touches materials/blendshapes/transforms: not pure
                    }
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (curve != null && curve.length > 0)
                    {
                        into[binding.path] = curve.keys[curve.length - 1].value > 0.5f;
                    }
                }
                return AnimationUtility.GetObjectReferenceCurveBindings(clip).Length == 0;
            }

            foreach (var state in states)
            {
                // Shape A: 1D blend tree on the parameter (Fury float toggle).
                if (state.motion is BlendTree tree)
                {
                    if (tree.blendType != BlendTreeType.Simple1D || tree.blendParameter != param)
                    {
                        return null;
                    }
                    var children = tree.children.OrderBy(c => c.threshold).ToArray();
                    foreach (var child in children)
                    {
                        if (!(child.motion is AnimationClip childClip))
                        {
                            return null;
                        }
                        bool isOn = child.threshold > 0.5f;
                        if (!Extract(childClip, isOn ? onValues : offValues))
                        {
                            return null;
                        }
                    }
                    continue;
                }

                if (!(state.motion is AnimationClip clip))
                {
                    if (state.motion == null)
                    {
                        continue;
                    }
                    return null;
                }

                // Shape B: motion-time state (curve sampled by the parameter itself).
                if (state.timeParameterActive && state.timeParameter == param)
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type != typeof(GameObject) || binding.propertyName != "m_IsActive")
                        {
                            return null;
                        }
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve == null || curve.length == 0)
                        {
                            continue;
                        }
                        offValues[binding.path] = curve.keys[0].value > 0.5f;
                        onValues[binding.path] = curve.keys[curve.length - 1].value > 0.5f;
                    }
                    continue;
                }

                // Shape C: plain ON/OFF states.
                if (!onness.TryGetValue(state, out var stateOn))
                {
                    // A state with curves we cannot attribute to on or off: play safe.
                    if (AnimationUtility.GetCurveBindings(clip).Length > 0)
                    {
                        return null;
                    }
                    continue;
                }
                if (!Extract(clip, stateOn ? onValues : offValues))
                {
                    return null;
                }
            }

            // Objects only touched by the OFF state would need inverse handling the CCK
            // toggle can't express; bail out and keep the animator layer for those.
            if (offValues.Keys.Any(path => !onValues.ContainsKey(path)))
            {
                return null;
            }

            var result = new List<TargetInfo>();
            foreach (var pair in onValues)
            {
                bool offState = offValues.TryGetValue(pair.Key, out var off) ? off : !pair.Value;
                if (offState == pair.Value)
                {
                    continue; // constant; nothing to toggle
                }
                result.Add(new TargetInfo { Path = pair.Key, OnState = pair.Value });
            }
            return result;
        }

        // ------------------------------------------------------------------- apply ----

        static bool ApplyTargets(BridgeContext ctx, CVRAdvancedSettingsEntry entry, List<TargetInfo> targets)
        {
            var toggle = (CVRAdvancesAvatarSettingGameObjectToggle)entry.setting;

            // Field layout differs slightly between CCK versions; reflection keeps this
            // compiling and lets us fall back to animator-based toggles cleanly.
            var field = toggle.GetType().GetField("gameObjectTargets", BindingFlags.Public | BindingFlags.Instance);
            if (field == null || !field.FieldType.IsGenericType)
            {
                if (!_targetsUnsupportedReported)
                {
                    ctx.Report.Warning(Category, "CCK toggle targets not found on this CCK version",
                        "Keeping animator-based toggles instead.");
                    _targetsUnsupportedReported = true;
                }
                return false;
            }

            var list = field.GetValue(toggle) as IList;
            if (list == null)
            {
                list = (IList)Activator.CreateInstance(field.FieldType);
                field.SetValue(toggle, list);
            }
            var elementType = field.FieldType.GetGenericArguments()[0];

            foreach (var target in targets)
            {
                var transform = ctx.Target.transform.Find(target.Path);
                if (transform == null)
                {
                    ctx.Report.Warning(Category, entry.name,
                        $"Toggle target \"{target.Path}\" not found on the avatar; skipped.");
                    continue;
                }
                var item = Activator.CreateInstance(elementType);
                TrySetMember(item, "gameObject", transform.gameObject);
                TrySetMember(item, "onState", target.OnState);
                TrySetMember(item, "treePath", target.Path);
                list.Add(item);
            }
            EditorUtility.SetDirty(ctx.CvrAvatar);
            return true;
        }

        static void TrySetMember(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && (value == null || field.FieldType.IsInstanceOfType(value) || field.FieldType == value.GetType()))
            {
                try { field.SetValue(target, value); } catch { }
            }
        }
    }
}
#endif
