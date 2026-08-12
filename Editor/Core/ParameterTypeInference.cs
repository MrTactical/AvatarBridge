#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    // Gives every parameter the animator type its own logic implies.
    //
    // VRCFury bakes menu parameters as FLOAT regardless of what they are, because a float
    // can carry a bool or an int and it saves it having to reason about intent. That is fine
    // in VRChat and wrong in ChilloutVR: CVR writes a menu value using the entry's declared
    // type, and writing a Bool into a Float animator parameter silently does nothing; the
    // classic "toggle does nothing in game" report.
    //
    // Two sources decide the real type, and neither is a guess:
    //
    //   1. The MENU CONTROL the parameter is bound to. A Toggle is a bool, a Dropdown is an
    //      int, a Slider is a float; that is what the author built, carried over from the
    //      VRChat menu. Advanced Settings entries are used rather than the VRChat expression
    //      parameters because their machineName is kept in step with the CCK-safe renames,
    //      so it still matches the animator by the time this runs.
    //
    //   2. For parameters with no menu control, how the ANIMATOR compares them. A parameter
    //      matched with Equals/NotEqual against whole numbers reaching 2 or more is a
    //      selector, whatever its declared type; nothing else about a float behaves that way.
    //
    // Against both sits one veto: anything read as a QUANTITY; a blend tree, motion time,
    // speed, cycle offset, or a parameter an animation clip writes; stays float, because
    // those need values between the whole numbers. The veto is checked first and is absolute.
    //
    // Conditions are rewritten to match whatever the parameter becomes, so no transition is
    // left comparing a bool with "> 0.5".
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
            RewriteConditions(master, changed, ctx);

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
            // alone; that shape is just as likely to be a float someone compares to 1.
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

        static void RewriteConditions(AnimatorController master,
            Dictionary<string, AnimatorControllerParameterType> changed, BridgeContext ctx)
        {
            int unreachableDropped = 0;
            var unreachableNotes = new List<string>();

            T[] Rewrite<T>(T[] transitions) where T : AnimatorTransitionBase
            {
                var kept = new List<T>(transitions.Length);
                foreach (var transition in transitions)
                {
                    var conditions = transition.conditions;
                    bool touched = false;
                    // A condition the parameter's new domain can NEVER satisfy was already dead
                    // in VRChat, and mapping its operator anyway would resurrect it; a "< 0"
                    // guard that never fired becomes "is false", which fires half the time. That
                    // is how a working avatar arrives with an animator layer that fights itself.
                    bool unreachable = false;
                    var surviving = new List<AnimatorCondition>(conditions.Length);
                    for (int i = 0; i < conditions.Length; i++)
                    {
                        if (!changed.TryGetValue(conditions[i].parameter, out var type))
                        {
                            surviving.Add(conditions[i]);
                            continue;
                        }
                        var condition = conditions[i];
                        if (type == AnimatorControllerParameterType.Bool)
                        {
                            // A bool only ever reads 0 or 1.
                            switch (condition.mode)
                            {
                                // Both ends of the range matter. "Unsatisfiable" was handled here
                                // from the start; TAUTOLOGY was not, and it is the one VRCFury
                                // actually writes: its remote branches are the band
                                // "IsLocal Greater -0.001 && IsLocal Less 0.001", meaning the
                                // value is 0. Turning "> -0.001" into "is true" made the pair say
                                // "true AND false", so every NonLocal state Fury generated became
                                // unreachable and the LOCAL branch played for remote viewers.
                                // Same misreading fixed in AnimatorMerger.ReconcileConditionModes
                                // in 3.5.26; this is the other retyping path, and the transitions
                                // that reach it are the ones that were still floats there.
                                case AnimatorConditionMode.Greater:
                                    if (condition.threshold >= 1f) { unreachable = true; }
                                    else if (condition.threshold < 0f) { touched = true; continue; }
                                    else { condition.mode = AnimatorConditionMode.If; }
                                    break;
                                case AnimatorConditionMode.Less:
                                    if (condition.threshold <= 0f) { unreachable = true; }
                                    else if (condition.threshold > 1f) { touched = true; continue; }
                                    else { condition.mode = AnimatorConditionMode.IfNot; }
                                    break;
                                case AnimatorConditionMode.Equals:
                                    if (Mathf.Approximately(condition.threshold, 0f)) { condition.mode = AnimatorConditionMode.IfNot; }
                                    else if (Mathf.Approximately(condition.threshold, 1f)) { condition.mode = AnimatorConditionMode.If; }
                                    else { unreachable = true; }
                                    break;
                                case AnimatorConditionMode.NotEqual:
                                    if (Mathf.Approximately(condition.threshold, 0f)) { condition.mode = AnimatorConditionMode.If; }
                                    else if (Mathf.Approximately(condition.threshold, 1f)) { condition.mode = AnimatorConditionMode.IfNot; }
                                    else
                                    {
                                        // "not a value it can ever hold" is always true: the
                                        // condition goes, the transition stays.
                                        touched = true;
                                        continue;
                                    }
                                    break;
                            }
                            if (unreachable)
                            {
                                break;
                            }
                            condition.threshold = 0f;
                        }
                        else
                        {
                            // Int comparisons are whole-number only, and If/IfNot don't exist
                            // for them; they become "not zero" / "is zero".
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
                        surviving.Add(condition);
                        touched = true;
                    }
                    if (unreachable)
                    {
                        unreachableDropped++;
                        if (unreachableNotes.Count < 5)
                        {
                            string to = transition.destinationState != null
                                ? transition.destinationState.name
                                : (transition.destinationStateMachine != null
                                    ? transition.destinationStateMachine.name : "Exit");
                            unreachableNotes.Add($"-> \"{to}\"");
                        }
                        continue;
                    }
                    if (touched)
                    {
                        transition.conditions = surviving.ToArray();
                    }
                    kept.Add(transition);
                }
                return kept.ToArray();
            }

            foreach (var layer in master.layers)
            {
                Walk(layer.stateMachine, machine =>
                {
                    machine.anyStateTransitions = Rewrite(machine.anyStateTransitions);
                    machine.entryTransitions = Rewrite(machine.entryTransitions);
                    foreach (var child in machine.states)
                    {
                        child.state.transitions = Rewrite(child.state.transitions);
                    }
                });
            }

            if (unreachableDropped > 0)
            {
                ctx.Report.Approximated("Animator",
                    $"{unreachableDropped} transition(s) dropped that could never fire",
                    $"{string.Join(", ", unreachableNotes)}{(unreachableDropped > unreachableNotes.Count ? ", …" : "")} " +
                    "— each rested on a numeric comparison the parameter's real range cannot satisfy (a " +
                    "\"less than zero\" guard on a value that is only ever 0 or 1, and similar), so it never " +
                    "fired in VRChat either. They are removed rather than translated because translating the " +
                    "operator alone would turn a transition that never fired into one that fires half the " +
                    "time — which makes a layer fight itself.");
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
