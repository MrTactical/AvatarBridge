#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    /// <summary>
    /// Gives every parameter the animator type its own logic implies.
    ///
    /// VRCFury bakes menu parameters as FLOAT regardless of what they are, because a float
    /// can carry a bool or an int and it saves it having to reason about intent. That is fine
    /// in VRChat and wrong in ChilloutVR: CVR writes a menu value using the entry's declared
    /// type, and writing a Bool into a Float animator parameter silently does nothing — the
    /// classic "toggle does nothing in game" report.
    ///
    /// Two sources decide the real type, and neither is a guess:
    ///
    ///   1. The MENU CONTROL the parameter is bound to. A Toggle is a bool, a Dropdown is an
    ///      int, a Slider is a float — that is what the author built, carried over from the
    ///      VRChat menu. Advanced Settings entries are used rather than the VRChat expression
    ///      parameters because their machineName is kept in step with the CCK-safe renames,
    ///      so it still matches the animator by the time this runs.
    ///
    ///   2. For parameters with no menu control, how the ANIMATOR compares them. A parameter
    ///      matched with Equals/NotEqual against whole numbers reaching 2 or more is a
    ///      selector, whatever its declared type; nothing else about a float behaves that way.
    ///
    /// Against both sits one veto: anything read as a QUANTITY — a blend tree, motion time,
    /// speed, cycle offset, or a parameter an animation clip writes — stays float, because
    /// those need values between the whole numbers. The veto is checked first and is absolute.
    ///
    /// Conditions are rewritten to match whatever the parameter becomes, so no transition is
    /// left comparing a bool with "&gt; 0.5".
    /// </summary>
    public static class ParameterTypeInference
    {
        const string Category = "Animator";

        public static void Run(AnimatorController master, BridgeContext ctx)
        {
            var quantity = new HashSet<string>();
            var exactMatches = new Dictionary<string, HashSet<float>>();
            Collect(master, quantity, exactMatches);

            var wanted = WantedTypes(ctx, exactMatches);
            var parameters = master.parameters;
            var changed = new Dictionary<string, AnimatorControllerParameterType>();
            var vetoed = new List<string>();

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                // Only VRCFury's float bake is corrected. A parameter the author already
                // declared Bool or Int is left exactly as it is.
                if (p.type != AnimatorControllerParameterType.Float
                    || !wanted.TryGetValue(p.name, out var target)
                    || target == AnimatorControllerParameterType.Float)
                {
                    continue;
                }
                if (quantity.Contains(p.name))
                {
                    vetoed.Add(p.name);
                    continue;
                }

                if (target == AnimatorControllerParameterType.Bool)
                {
                    p.defaultBool = p.defaultFloat != 0f;
                }
                else
                {
                    p.defaultInt = Mathf.RoundToInt(p.defaultFloat);
                }
                p.type = target;
                changed[p.name] = target;
            }

            if (changed.Count == 0)
            {
                ReportVetoes(ctx, vetoed);
                return;
            }
            master.parameters = parameters;
            RewriteConditions(master, changed);

            int bools = changed.Count(c => c.Value == AnimatorControllerParameterType.Bool);
            int ints = changed.Count - bools;
            var parts = new List<string>();
            if (bools > 0)
            {
                parts.Add($"{bools} to Bool");
            }
            if (ints > 0)
            {
                parts.Add($"{ints} to Int");
            }
            ctx.Report.Converted(Category, $"{changed.Count} parameter(s) retyped from Float ({string.Join(", ", parts)})",
                "VRCFury bakes every menu parameter as a float; ChilloutVR writes menu values using the " +
                "entry's own type, so a bool written into a float parameter does nothing and the control " +
                "looks dead in game. Each one was retyped to what its menu control and animator conditions " +
                "actually use, and its transitions were rewritten to match.");
            ReportVetoes(ctx, vetoed);
        }

        static void ReportVetoes(BridgeContext ctx, List<string> vetoed)
        {
            if (vetoed.Count == 0)
            {
                return;
            }
            ctx.Report.Approximated(Category, $"{vetoed.Count} parameter(s) kept as Float",
                $"{string.Join(", ", vetoed.Take(6))}{(vetoed.Count > 6 ? ", …" : "")} — their menu control " +
                "suggests a bool or an int, but a blend tree, motion time or animation clip reads them as a " +
                "quantity, which needs the values in between. Retyping would have broken that.");
        }

        /// <summary>
        /// What each parameter should be: the menu control it drives, or failing that, the
        /// shape of the comparisons made against it.
        /// </summary>
        static Dictionary<string, AnimatorControllerParameterType> WantedTypes(
            BridgeContext ctx, Dictionary<string, HashSet<float>> exactMatches)
        {
            var wanted = new Dictionary<string, AnimatorControllerParameterType>();

            var settings = ctx.CvrAvatar != null && ctx.CvrAvatar.avatarSettings != null
                ? ctx.CvrAvatar.avatarSettings.settings
                : null;
            if (settings != null)
            {
                foreach (var entry in settings)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.machineName))
                    {
                        continue;
                    }
                    switch (entry.type)
                    {
                        case CVRAdvancedSettingsEntry.SettingsType.Toggle:
                            wanted[entry.machineName] = AnimatorControllerParameterType.Bool;
                            break;
                        case CVRAdvancedSettingsEntry.SettingsType.Dropdown:
                            wanted[entry.machineName] = AnimatorControllerParameterType.Int;
                            break;
                        default:
                            // Sliders, joysticks and vector inputs are genuinely continuous.
                            wanted[entry.machineName] = AnimatorControllerParameterType.Float;
                            break;
                    }
                }
            }

            // No menu control: a parameter only ever matched exactly, against whole numbers
            // that reach 2 or more, is a selector. One matched only against 0 and 1 is left
            // alone — that shape is just as likely to be a float someone compares to 1.
            foreach (var pair in exactMatches)
            {
                if (wanted.ContainsKey(pair.Key))
                {
                    continue;
                }
                if (pair.Value.Count > 0 && pair.Value.All(IsWhole) && pair.Value.Any(v => v >= 2f))
                {
                    wanted[pair.Key] = AnimatorControllerParameterType.Int;
                }
            }
            return wanted;
        }

        static bool IsWhole(float value)
        {
            return Mathf.Approximately(value, Mathf.Round(value));
        }

        /// <summary>
        /// Walks the controller for the two things that matter: parameters read as quantities
        /// (which can never be retyped) and the thresholds of exact comparisons.
        /// </summary>
        static void Collect(AnimatorController master, HashSet<string> quantity,
            Dictionary<string, HashSet<float>> exactMatches)
        {
            void NoteMotion(Motion motion)
            {
                if (motion is BlendTree tree)
                {
                    quantity.Add(tree.blendParameter);
                    quantity.Add(tree.blendParameterY);
                    foreach (var child in tree.children)
                    {
                        if (tree.blendType == BlendTreeType.Direct)
                        {
                            quantity.Add(child.directBlendParameter);
                        }
                        NoteMotion(child.motion);
                    }
                }
                else if (motion is AnimationClip clip)
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        // An animated animator parameter: the clip drives it over time.
                        if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path))
                        {
                            quantity.Add(binding.propertyName);
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
                        if (condition.mode != AnimatorConditionMode.Equals
                            && condition.mode != AnimatorConditionMode.NotEqual)
                        {
                            continue;
                        }
                        if (!exactMatches.TryGetValue(condition.parameter, out var values))
                        {
                            values = new HashSet<float>();
                            exactMatches[condition.parameter] = values;
                        }
                        values.Add(condition.threshold);
                    }
                }
            }

            foreach (var layer in master.layers)
            {
                Walk(layer.stateMachine, machine =>
                {
                    NoteConditions(machine.anyStateTransitions);
                    NoteConditions(machine.entryTransitions);
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        if (state.timeParameterActive) quantity.Add(state.timeParameter);
                        if (state.speedParameterActive) quantity.Add(state.speedParameter);
                        if (state.cycleOffsetParameterActive) quantity.Add(state.cycleOffsetParameter);
                        if (state.mirrorParameterActive) quantity.Add(state.mirrorParameter);
                        NoteMotion(state.motion);
                        NoteConditions(state.transitions);
                    }
                });
            }
        }

        /// <summary>
        /// Brings every condition into line with its parameter's new type, so nothing is left
        /// comparing a bool numerically or an int fractionally.
        /// </summary>
        static void RewriteConditions(AnimatorController master,
            Dictionary<string, AnimatorControllerParameterType> changed)
        {
            void Rewrite(AnimatorTransitionBase[] transitions)
            {
                foreach (var transition in transitions)
                {
                    var conditions = transition.conditions;
                    bool touched = false;
                    for (int i = 0; i < conditions.Length; i++)
                    {
                        if (!changed.TryGetValue(conditions[i].parameter, out var type))
                        {
                            continue;
                        }
                        var condition = conditions[i];
                        if (type == AnimatorControllerParameterType.Bool)
                        {
                            switch (condition.mode)
                            {
                                case AnimatorConditionMode.Greater:
                                    condition.mode = AnimatorConditionMode.If;
                                    break;
                                case AnimatorConditionMode.Less:
                                    condition.mode = AnimatorConditionMode.IfNot;
                                    break;
                                case AnimatorConditionMode.Equals:
                                    condition.mode = condition.threshold != 0f
                                        ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
                                    break;
                                case AnimatorConditionMode.NotEqual:
                                    condition.mode = condition.threshold != 0f
                                        ? AnimatorConditionMode.IfNot : AnimatorConditionMode.If;
                                    break;
                            }
                            condition.threshold = 0f;
                        }
                        else
                        {
                            // Int comparisons are whole-number only, and If/IfNot don't exist
                            // for them — they become "not zero" / "is zero".
                            switch (condition.mode)
                            {
                                case AnimatorConditionMode.If:
                                    condition.mode = AnimatorConditionMode.Greater;
                                    condition.threshold = 0f;
                                    break;
                                case AnimatorConditionMode.IfNot:
                                    condition.mode = AnimatorConditionMode.Less;
                                    condition.threshold = 1f;
                                    break;
                                default:
                                    condition.threshold = Mathf.Round(condition.threshold);
                                    break;
                            }
                        }
                        conditions[i] = condition;
                        touched = true;
                    }
                    if (touched)
                    {
                        transition.conditions = conditions;
                    }
                }
            }

            foreach (var layer in master.layers)
            {
                Walk(layer.stateMachine, machine =>
                {
                    Rewrite(machine.anyStateTransitions);
                    Rewrite(machine.entryTransitions);
                    foreach (var child in machine.states)
                    {
                        Rewrite(child.state.transitions);
                    }
                });
            }
        }

        static void Walk(AnimatorStateMachine machine, System.Action<AnimatorStateMachine> visit)
        {
            if (machine == null)
            {
                return;
            }
            visit(machine);
            foreach (var child in machine.stateMachines)
            {
                Walk(child.stateMachine, visit);
            }
        }
    }
}
#endif
