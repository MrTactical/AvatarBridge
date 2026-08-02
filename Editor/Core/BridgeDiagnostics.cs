#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ABI.CCK.Components;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    /// <summary>
    /// Runs last, and does two things the rest of the converter doesn't.
    ///
    /// **Validates.** Every check here is for something that produces a silently broken avatar
    /// rather than an error — a condition on a parameter that no longer exists, a menu entry
    /// whose type can't drive its parameter, a cloth with no bones. ChilloutVR does not complain
    /// about any of these; it just quietly does nothing, which is how they reach a user.
    ///
    /// **Dumps.** Every bug diagnosed on this project so far started with someone grepping the
    /// generated .controller by hand: does anything read this parameter, what compares it, what
    /// do the drivers write. Those answers now go into the report itself, so a bug report is
    /// answerable without a round trip asking for the controller.
    /// </summary>
    public static class BridgeDiagnostics
    {
        const string Category = "Diagnostics";

        /// <summary>How a parameter is touched, which is the question that keeps coming up.</summary>
        class Usage
        {
            public int Conditions, BlendTrees, MotionTime, DriverWrites, DriverReads, ClipWrites;
            public readonly HashSet<string> ConditionModes = new HashSet<string>();
            public bool Any => Conditions + BlendTrees + MotionTime + DriverWrites + DriverReads + ClipWrites > 0;
        }

        public static void Run(BridgeContext ctx, AnimatorController master)
        {
            if (master == null)
            {
                return;
            }
            var usage = CollectUsage(master);
            Validate(ctx, master, usage);
            CheckRemoteDefaultLoops(ctx, master);
            CheckStuckStates(ctx, master);
            ctx.Report.Appendix = BuildAppendix(ctx, master, usage);
        }

        // ------------------------------------------------------------------ validation ----

        static void Validate(BridgeContext ctx, AnimatorController master, Dictionary<string, Usage> usage)
        {
            var declared = new HashSet<string>(master.parameters.Select(p => p.name));

            // A condition on an undeclared parameter: Unity keeps the transition, ChilloutVR
            // drops it, and the layer silently never advances.
            var undeclared = usage.Keys.Where(k => !string.IsNullOrEmpty(k) && !declared.Contains(k)).ToList();
            if (undeclared.Count > 0)
            {
                ctx.Report.Warning(Category, $"{undeclared.Count} parameter(s) used but never declared",
                    $"{Join(undeclared)} — transitions and drivers reference these, but the animator has no " +
                    "such parameter. ChilloutVR drops those transitions, so whatever they gate never happens.");
            }

            // Declared but inert. Not broken, but it is how a dead menu entry or a half-stripped
            // system shows up, and it costs sync bits if it's synced.
            //
            // Parameters ChilloutVR drives itself are excluded: the CCK's own animator declares
            // GestureLeft, Grounded, IsLocal and friends whether or not this avatar's logic reads
            // them, so flagging those would fire on every single conversion — and a check that
            // always fires is a check people learn to scroll past.
            var inert = master.parameters
                .Where(p => !usage.ContainsKey(p.name) || !usage[p.name].Any)
                .Select(p => p.name)
                .Where(n => !n.StartsWith("#") && !IsPlatformDriven(n))
                .ToList();
            if (inert.Count > 0)
            {
                ctx.Report.Approximated(Category, $"{inert.Count} parameter(s) declared but never read",
                    $"{Join(inert)} — nothing in the animator conditions on, blends with, drives or animates " +
                    "these. Usually a leftover from a layer that wasn't converted or was stripped.");
            }

            CheckSyncBudget(ctx, master);
            CheckComponentWhitelist(ctx);
            CheckStereoShaders(ctx);

            // A cloth with nothing to simulate.
            int emptyCloths = 0;
            foreach (var mono in ctx.Target.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mono == null || mono.GetType().Name != "MagicaCloth")
                {
                    continue;
                }
                var sdata = mono.GetType().GetProperty("SerializeData")?.GetValue(mono);
                var roots = sdata?.GetType().GetField("rootBones")?.GetValue(sdata) as System.Collections.IList;
                if (roots != null && roots.Count == 0)
                {
                    emptyCloths++;
                }
            }
            if (emptyCloths > 0)
            {
                ctx.Report.Warning(Category, $"{emptyCloths} cloth component(s) have no root bones",
                    "They will simulate nothing. Usually means the source PhysBone's root was removed by a " +
                    "strip pass after the cloth was created.");
            }

            // Two layers with one name is legal in Unity and ambiguous everywhere else.
            var dupLayers = master.layers.GroupBy(l => l.name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupLayers.Count > 0)
            {
                ctx.Report.Warning(Category, $"{dupLayers.Count} duplicate layer name(s)",
                    $"{Join(dupLayers)} — duplicated names make the animator ambiguous to read and to debug.");
            }

            ValidateMenu(ctx, master, declared);
        }

        static void ValidateMenu(BridgeContext ctx, AnimatorController master, HashSet<string> declared)
        {
            var settings = ctx.CvrAvatar != null && ctx.CvrAvatar.avatarSettings != null
                ? ctx.CvrAvatar.avatarSettings.settings
                : null;
            if (settings == null)
            {
                return;
            }
            var types = master.parameters.ToDictionary(p => p.name, p => p.type);
            var mismatched = new List<string>();

            foreach (var entry in settings)
            {
                if (entry == null || string.IsNullOrEmpty(entry.machineName) || entry.setting == null
                    || !types.TryGetValue(entry.machineName, out var animType))
                {
                    continue;
                }
                // ChilloutVR writes a menu value using the ENTRY's type. If that disagrees with
                // the animator's, the write lands nowhere and the control looks dead in game.
                var wanted = animType == AnimatorControllerParameterType.Int
                    ? CVRAdvancesAvatarSettingBase.ParameterType.Int
                    : animType == AnimatorControllerParameterType.Bool
                        ? CVRAdvancesAvatarSettingBase.ParameterType.Bool
                        : CVRAdvancesAvatarSettingBase.ParameterType.Float;
                if (entry.setting.usedType != wanted)
                {
                    mismatched.Add($"{entry.machineName} (menu {entry.setting.usedType}, animator {animType})");
                }
            }
            if (mismatched.Count > 0)
            {
                ctx.Report.Warning(Category, $"{mismatched.Count} menu entr(ies) disagree with their parameter's type",
                    $"{Join(mismatched)} — ChilloutVR writes menu values using the entry's own type, so a " +
                    "mismatch writes nothing and the control does nothing in game.");
            }
        }

        // -------------------------------------------------------------------- appendix ----

        static string BuildAppendix(BridgeContext ctx, AnimatorController master, Dictionary<string, Usage> usage)
        {
            var sb = new StringBuilder();

            sb.AppendLine("### Settings used");
            sb.AppendLine();
            var s = ctx.Settings;
            sb.AppendLine($"- physics target: `{s.physicsTarget}` · presets: `{s.useMagicaPresets}` · " +
                          $"fit to PhysBone: `{s.fitToPhysBone}` · cap radius: `{s.capParticleRadius}` · " +
                          $"angle limits: `{s.transferAngleLimits}` · auto colliders: `{s.autoAssignNearbyColliders}`");
            sb.AppendLine($"- strip GoGo: `{s.stripGogoLoco}` · strip SPS: `{s.stripSpsSystems}` · " +
                          $"face tracking: `{s.faceTrackingMode}` · scaler: `{s.addAvatarScaler}`");
            sb.AppendLine();

            sb.AppendLine("### Animator layers");
            sb.AppendLine();
            sb.AppendLine("| # | layer | states | weight |");
            sb.AppendLine("|---|---|---|---|");
            for (int i = 0; i < master.layers.Length; i++)
            {
                var l = master.layers[i];
                sb.AppendLine($"| {i} | `{l.name}` | {CountStates(l.stateMachine)} | {l.defaultWeight:0.##} |");
            }
            sb.AppendLine();

            sb.AppendLine("### Parameters and what reads them");
            sb.AppendLine();
            sb.AppendLine("`cond` = transition conditions · `tree` = blend trees · `time` = motion time/speed · " +
                          "`drv w/r` = driver writes/reads · `clip` = animated by a clip");
            sb.AppendLine();
            sb.AppendLine("| parameter | type | default | cond | tree | time | drv w | drv r | clip | modes |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var p in master.parameters)
            {
                usage.TryGetValue(p.name, out var u);
                u = u ?? new Usage();
                string def = p.type == AnimatorControllerParameterType.Bool ? p.defaultBool.ToString()
                    : p.type == AnimatorControllerParameterType.Int ? p.defaultInt.ToString()
                    : p.defaultFloat.ToString("0.##");
                sb.AppendLine($"| `{p.name}` | {p.type} | {def} | {u.Conditions} | {u.BlendTrees} | " +
                              $"{u.MotionTime} | {u.DriverWrites} | {u.DriverReads} | {u.ClipWrites} | " +
                              $"{(u.ConditionModes.Count > 0 ? string.Join(" ", u.ConditionModes.OrderBy(x => x)) : "—")} |");
            }
            sb.AppendLine();

            AppendMenu(sb, ctx);
            AppendPhysics(sb, ctx);
            AppendFaceMesh(sb, ctx);
            return sb.ToString();
        }

        static void AppendMenu(StringBuilder sb, BridgeContext ctx)
        {
            var settings = ctx.CvrAvatar != null && ctx.CvrAvatar.avatarSettings != null
                ? ctx.CvrAvatar.avatarSettings.settings
                : null;
            if (settings == null || settings.Count == 0)
            {
                return;
            }
            sb.AppendLine("### Menu entries");
            sb.AppendLine();
            sb.AppendLine("| name | parameter | control | value type | options |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var e in settings)
            {
                if (e == null)
                {
                    continue;
                }
                int options = e.setting is CVRAdvancesAvatarSettingGameObjectDropdown dd && dd.options != null
                    ? dd.options.Count : 0;
                sb.AppendLine($"| {e.name} | `{e.machineName}` | {e.type} | " +
                              $"{(e.setting != null ? e.setting.usedType.ToString() : "—")} | " +
                              $"{(options > 0 ? options.ToString() : "—")} |");
            }
            sb.AppendLine();
        }

        static void AppendPhysics(StringBuilder sb, BridgeContext ctx)
        {
            var cloths = new List<string>();
            foreach (var mono in ctx.Target.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mono != null && mono.GetType().Name == "MagicaCloth")
                {
                    cloths.Add($"| `{mono.gameObject.name}` | {(mono.gameObject.activeSelf ? "on" : "off")} |");
                }
            }
            if (cloths.Count == 0)
            {
                return;
            }
            sb.AppendLine($"### Cloth components ({cloths.Count})");
            sb.AppendLine();
            sb.AppendLine("Per-chain class, preset and source PhysBone values are in the " +
                          "*PhysBones → MagicaCloth2* section above.");
            sb.AppendLine();
            sb.AppendLine("| object | starts |");
            sb.AppendLine("|---|---|");
            foreach (string c in cloths)
            {
                sb.AppendLine(c);
            }
            sb.AppendLine();
        }

        static void AppendFaceMesh(StringBuilder sb, BridgeContext ctx)
        {
            var face = ctx.CvrAvatar != null ? ctx.CvrAvatar.bodyMesh : null;
            if (face == null || face.sharedMesh == null)
            {
                return;
            }
            sb.AppendLine("### Face mesh");
            sb.AppendLine();
            sb.AppendLine($"- `{face.name}` — {face.sharedMesh.blendShapeCount} blendshape(s)");
            var blink = new List<string>();
            for (int i = 0; i < face.sharedMesh.blendShapeCount; i++)
            {
                string n = face.sharedMesh.GetBlendShapeName(i);
                if (n.ToLowerInvariant().Contains("blink"))
                {
                    blink.Add(n);
                }
            }
            if (blink.Count > 0)
            {
                sb.AppendLine($"- blink-ish shapes present: {Join(blink)}");
            }
            if (ctx.CvrAvatar.blinkBlendshape != null)
            {
                var wired = ctx.CvrAvatar.blinkBlendshape.Where(b => !string.IsNullOrEmpty(b)).ToList();
                sb.AppendLine($"- wired to: {(wired.Count > 0 ? Join(wired) : "none")}");
            }
            sb.AppendLine();
        }

        // ----------------------------------------------------------------- collection ----

        static Dictionary<string, Usage> CollectUsage(AnimatorController master)
        {
            var map = new Dictionary<string, Usage>();
            // An "animated animator parameter" binding is type Animator with an empty path — but
            // so is every humanoid MUSCLE curve Unity bakes into a locomotion clip: RootQ.x,
            // LeftFootT.y, unknown_2 and hundreds more. They are not parameters and never appear
            // in master.parameters, so requiring the name to be declared separates the two. A
            // clip writing to an undeclared parameter does nothing anyway.
            var declaredNames = new HashSet<string>(master.parameters.Select(p => p.name));
            Usage For(string name)
            {
                if (string.IsNullOrEmpty(name))
                {
                    return new Usage(); // scratch, discarded
                }
                if (!map.TryGetValue(name, out var u))
                {
                    u = new Usage();
                    map[name] = u;
                }
                return u;
            }

            void NoteMotion(Motion motion)
            {
                if (motion is BlendTree tree)
                {
                    // Count only the axis fields this blend type reads — Direct trees read
                    // neither, 1D trees only X. The leftover "Blend"/"Smooth Amount"/"Value"
                    // names on Direct trees otherwise show up in this table as live references
                    // on every avatar, which is how they ended up in every bug report.
                    if (tree.blendType != BlendTreeType.Direct)
                    {
                        For(tree.blendParameter).BlendTrees++;
                        if (tree.blendType != BlendTreeType.Simple1D)
                        {
                            For(tree.blendParameterY).BlendTrees++;
                        }
                    }
                    foreach (var child in tree.children)
                    {
                        if (tree.blendType == BlendTreeType.Direct)
                        {
                            For(child.directBlendParameter).BlendTrees++;
                        }
                        NoteMotion(child.motion);
                    }
                }
                else if (motion is AnimationClip clip)
                {
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (b.type == typeof(Animator) && string.IsNullOrEmpty(b.path)
                            && declaredNames.Contains(b.propertyName))
                        {
                            For(b.propertyName).ClipWrites++;
                        }
                    }
                }
            }

            void NoteConditions(AnimatorTransitionBase[] transitions)
            {
                foreach (var t in transitions)
                {
                    foreach (var c in t.conditions)
                    {
                        var u = For(c.parameter);
                        u.Conditions++;
                        u.ConditionModes.Add(c.mode.ToString());
                    }
                }
            }

            void NoteDrivers(IEnumerable<StateMachineBehaviour> behaviours)
            {
#if CVR_CCK_EXISTS
                foreach (var b in behaviours)
                {
                    if (b == null || b.GetType().Name != "AnimatorDriver")
                    {
                        continue;
                    }
                    foreach (string listName in new[] { "EnterTasks", "ExitTasks" })
                    {
                        var list = b.GetType().GetField(listName)?.GetValue(b) as System.Collections.IEnumerable;
                        if (list == null)
                        {
                            continue;
                        }
                        foreach (var task in list)
                        {
                            var tt = task.GetType();
                            For(tt.GetField("targetName")?.GetValue(task) as string).DriverWrites++;
                            For(tt.GetField("aName")?.GetValue(task) as string).DriverReads++;
                            For(tt.GetField("bName")?.GetValue(task) as string).DriverReads++;
                        }
                    }
                }
#endif
            }

            foreach (var layer in master.layers)
            {
                Walk(layer.stateMachine, machine =>
                {
                    NoteConditions(machine.anyStateTransitions);
                    NoteConditions(machine.entryTransitions);
                    NoteDrivers(machine.behaviours);
                    foreach (var child in machine.states)
                    {
                        var st = child.state;
                        if (st.timeParameterActive) For(st.timeParameter).MotionTime++;
                        if (st.speedParameterActive) For(st.speedParameter).MotionTime++;
                        if (st.cycleOffsetParameterActive) For(st.cycleOffsetParameter).MotionTime++;
                        if (st.mirrorParameterActive) For(st.mirrorParameter).MotionTime++;
                        NoteMotion(st.motion);
                        NoteConditions(st.transitions);
                        NoteDrivers(st.behaviours);
                    }
                });
            }
            return map;
        }

        /// <summary>
        /// Finds layers that re-enter a state every frame once every parameter is at its
        /// SERIALIZED DEFAULT — the state a remote copy of the avatar is in.
        ///
        /// Remote copies differ from the wearer's in ways that all point the same direction: "#"
        /// local parameters never sync and sit at their defaults forever, CVRParameterStream is
        /// stripped from remote copies, localOnly drivers don't run, and at load NOTHING has
        /// replicated yet — so for the first seconds every parameter reads its default. A layer
        /// the wearer never sees move, because their live value parks it, can at those defaults
        /// satisfy a loop of transitions and thrash.
        ///
        /// Reported by a tester as a body cycling through every colour with a pulsing outline,
        /// for about thirty seconds after putting the avatar on, visible ONLY to other people —
        /// two material properties driven by one runaway layer, ending the moment the real values
        /// replicated. The wearer's own hue slider never moved, which is exactly why this is worth
        /// a static check: the author cannot see it, cannot reproduce it, and the avatar is
        /// correct on their screen the entire time.
        ///
        /// Only INSTANT re-entry counts. A cycle whose transitions all wait on exit time is an
        /// animation sequence playing in order, which is what sequences are for; a cycle where
        /// some transition fires with no exit time re-evaluates the same frame and never settles.
        /// </summary>
        static void CheckRemoteDefaultLoops(BridgeContext ctx, AnimatorController master)
        {
            var defaults = new Dictionary<string, AnimatorControllerParameter>(StringComparer.Ordinal);
            foreach (var p in master.parameters)
            {
                defaults[p.name] = p;
            }

            // A condition that cannot be judged (parameter missing) is treated as NOT satisfied,
            // so an unknown never manufactures a loop that isn't there.
            bool Satisfied(AnimatorCondition c)
            {
                // A serialized default only describes a parameter NOTHING drives. ChilloutVR
                // drives its core parameters on every copy, remote ones included, so reading
                // their defaults here describes no machine that exists.
                //
                // IsLocal is the one with an answer rather than an unknown: this check is about
                // the remote copy, and on a remote copy IsLocal is FALSE by definition. Reading
                // its declared default (1, the resting value given for the WEARER) inverted every
                // local/remote gate on the avatar and turned VRCFury's Remote Trap — a state that
                // exists to hold a layer still on remotes — into a reported thrash. Three of the
                // first five hits were that mistake.
                string bare = c.parameter.TrimStart('#');
                if (bare == "IsLocal")
                {
                    return c.mode == AnimatorConditionMode.IfNot;
                }
                // Swimming and AFK join the core set here: the client writes both, which is the
                // only property that matters for this check, even though it does not mark them
                // core (they still cost sync bits, which is why ClientCoreParameters omits them).
                if (ClientCoreParameters.Contains(bare) || bare == "Swimming" || bare == "AFK")
                {
                    return false;   // client-driven and live; unknowable here, so never a loop
                }
                if (!defaults.TryGetValue(c.parameter, out var p))
                {
                    return false;
                }
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        return c.mode == AnimatorConditionMode.If ? p.defaultBool : !p.defaultBool;
                    case AnimatorControllerParameterType.Trigger:
                        return false;   // needs a set, which nothing does at defaults
                    case AnimatorControllerParameterType.Int:
                        switch (c.mode)
                        {
                            case AnimatorConditionMode.Greater:  return p.defaultInt > c.threshold;
                            case AnimatorConditionMode.Less:     return p.defaultInt < c.threshold;
                            case AnimatorConditionMode.Equals:   return p.defaultInt == (int)c.threshold;
                            case AnimatorConditionMode.NotEqual: return p.defaultInt != (int)c.threshold;
                            default: return false;
                        }
                    case AnimatorControllerParameterType.Float:
                        switch (c.mode)
                        {
                            case AnimatorConditionMode.Greater: return p.defaultFloat > c.threshold;
                            case AnimatorConditionMode.Less:    return p.defaultFloat < c.threshold;
                            default: return false;
                        }
                    default:
                        return false;
                }
            }

            bool FiresAtDefaults(AnimatorStateTransition t, out bool instant)
            {
                instant = !t.hasExitTime || t.exitTime <= 0.01f;
                if (t.destinationState == null && t.destinationStateMachine == null)
                {
                    return false;   // exit transitions leave the machine; not a loop edge here
                }
                foreach (var c in t.conditions)
                {
                    if (!Satisfied(c))
                    {
                        return false;
                    }
                }
                return true;
            }

            var looping = new List<string>();
            foreach (var layer in master.layers)
            {
                if (layer == null || layer.stateMachine == null)
                {
                    continue;
                }

                // Edges that fire at defaults, plus which of them re-evaluate the same frame.
                var edges = new Dictionary<AnimatorState, List<AnimatorState>>();
                var instantEdge = new HashSet<(AnimatorState, AnimatorState)>();
                var anyStateTargets = new List<AnimatorState>();
                string selfLoop = null;

                Walk(layer.stateMachine, machine =>
                {
                    // AnyState re-entering a state it is already in, with nothing to stop it, is
                    // the classic form and needs no cycle search.
                    foreach (var t in machine.anyStateTransitions)
                    {
                        if (t == null || !FiresAtDefaults(t, out bool anyInstant)) continue;
                        if (t.destinationState != null)
                        {
                            anyStateTargets.Add(t.destinationState);
                        }
                        if (anyInstant && t.canTransitionToSelf && t.destinationState != null && selfLoop == null)
                        {
                            selfLoop = t.destinationState.name;
                        }
                    }
                    foreach (var child in machine.states)
                    {
                        var from = child.state;
                        if (from == null) continue;
                        foreach (var t in from.transitions)
                        {
                            if (t == null || t.destinationState == null) continue;
                            if (!FiresAtDefaults(t, out bool inst)) continue;
                            if (!edges.TryGetValue(from, out var to))
                            {
                                edges[from] = to = new List<AnimatorState>();
                            }
                            to.Add(t.destinationState);
                            if (inst)
                            {
                                instantEdge.Add((from, t.destinationState));
                            }
                        }
                    }
                });

                // A cycle only matters if the layer can REACH it at these values. VRCFury parks
                // its generated layers in a "Remote Trap" state whose only exit tests IsLocal —
                // false on a remote copy, so the layer never leaves and the busy little cycle of
                // driver states behind it never runs. Fury is defending against this exact bug,
                // and without a reachability check the defence reads as the bug: the trap was
                // three of the first ten hits, all of them wrong.
                var reachable = new HashSet<AnimatorState>();
                var queue = new Queue<AnimatorState>();
                void Reach(AnimatorState s)
                {
                    if (s != null && reachable.Add(s))
                    {
                        queue.Enqueue(s);
                    }
                }
                Reach(layer.stateMachine.defaultState);
                foreach (var s in anyStateTargets)
                {
                    Reach(s);   // AnyState ignores where the layer currently is
                }
                while (queue.Count > 0)
                {
                    var at = queue.Dequeue();
                    if (edges.TryGetValue(at, out var outs))
                    {
                        foreach (var to in outs) Reach(to);
                    }
                }
                foreach (var key in edges.Keys.ToList())
                {
                    if (!reachable.Contains(key))
                    {
                        edges.Remove(key);
                    }
                }

                string found = selfLoop;
                if (found == null && edges.Count > 0)
                {
                    // Depth-first cycle search. A cycle only counts if at least one of its edges
                    // is instant — otherwise every step waits for a clip to finish and the layer
                    // is a sequence, not a thrash.
                    var state = new Dictionary<AnimatorState, int>();   // 0 unseen, 1 on stack, 2 done
                    var stack = new List<AnimatorState>();
                    bool Visit(AnimatorState node)
                    {
                        state[node] = 1;
                        stack.Add(node);
                        if (edges.TryGetValue(node, out var next))
                        {
                            foreach (var to in next)
                            {
                                state.TryGetValue(to, out int mark);
                                if (mark == 1)
                                {
                                    int at = stack.IndexOf(to);
                                    // Cannot be -1 while the mark says "on stack", but a throw
                                    // here would abort a conversion over a diagnostic.
                                    for (int i = at < 0 ? stack.Count : at; i < stack.Count; i++)
                                    {
                                        var a = stack[i];
                                        var b = i + 1 < stack.Count ? stack[i + 1] : to;
                                        if (instantEdge.Contains((a, b)))
                                        {
                                            found = to.name;
                                            return true;
                                        }
                                    }
                                }
                                else if (mark == 0 && Visit(to))
                                {
                                    return true;
                                }
                            }
                        }
                        state[node] = 2;
                        stack.RemoveAt(stack.Count - 1);
                        return false;
                    }
                    foreach (var node in edges.Keys.ToList())
                    {
                        if (found != null) break;
                        state.TryGetValue(node, out int mark);
                        if (mark == 0)
                        {
                            Visit(node);
                        }
                    }
                }

                if (found != null)
                {
                    looping.Add($"\"{layer.name}\" (at \"{found}\")");
                }
            }

            if (looping.Count > 0)
            {
                ctx.Report.Warning(Category,
                    $"{looping.Count} layer(s) may thrash on OTHER players' screens right after the " +
                    "avatar loads",
                    string.Join("; ", looping) + " — with every parameter at its default, these layers " +
                    "re-enter a state on the same frame instead of settling, so anything they drive " +
                    "(colours, blendshapes, toggles, outlines) flickers or cycles. YOU WILL NOT SEE " +
                    "THIS: on your own copy your live parameter values park the layer, and it is your " +
                    "copy the Unity editor previews. Remote copies start at the serialized defaults " +
                    "and stay there until your values replicate — seconds after load, longer for a " +
                    "value you never touch — which is why this looks like a rare bug that fixes " +
                    "itself. Set the DEFAULT of the parameters these layers read to a value that " +
                    "parks them (usually the resting position of whatever they drive), or give the " +
                    "looping transition an exit time so it cannot fire twice in a frame. The CCK " +
                    "Animator Tester's Remote view card reproduces this locally.");
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

        static int CountStates(AnimatorStateMachine machine)
        {
            int n = 0;
            Walk(machine, m => n += m.states.Length);
            return n;
        }

        /// <summary>
        /// Parameters the CCK's own base animator declares, which ChilloutVR writes from the
        /// player rather than the avatar's logic. Kept here ungated because Setup mode has no
        /// VRChat SDK and still copies that animator; the conversion path additionally defers to
        /// <see cref="AnimatorMerger.IsGameDrivenParameter"/>, which knows about the
        /// stream-fed ones too.
        /// </summary>
        /// <summary>
        /// Every component type ChilloutVR keeps on an avatar, transcribed from the client's
        /// SharedFilter whitelists and the conditional branches in AssetFilter.FilterAvatar.
        ///
        /// The client walks GetComponentsInChildren over the whole avatar and calls
        /// DestroyComponentWithRequirements on anything it doesn't recognise. There is no message,
        /// no fallback, and nothing about the converted asset looks wrong beforehand — the
        /// component is simply gone the moment the avatar loads. A quadruped arrived with ten
        /// GrounderVRIK components driving its leg placement, none of which appear in any list.
        ///
        /// The union deliberately includes types allowed only conditionally, on a viewer setting
        /// (audio, lights, cameras), and the local-only set that survives on the wearer's copy but
        /// not on remote ones. Flagging those would fire constantly and teach people to ignore
        /// this. Only components with no route through the filter at all are reported.
        /// </summary>
        static readonly HashSet<string> CvrAvatarComponentWhitelist = new HashSet<string>
        {
            // RootComponents — permitted anywhere, despite the name.
            "Animator", "CVRAssetInfo", "CVRLuaClientBehaviour", "LookAtIK", "Transform",
            "TwistRelaxer", "VRIK", "WasmRuntimeBehaviour", "WasmVMAnchor",
            // AvatarWhitelist.
            "AimConstraint", "AimIK", "BipedIK", "CCDIK", "CharacterJoint", "ConfigurableJoint",
            "ConstantForce", "ContactAnimator", "ContactReceiver", "ContactSender",
            "CVRAdvancedAvatarSettingsPointer", "CVRAnimatorDriver", "CVRAudioDriver",
            "CVRCameraHelper", "CVRDataStore", "CVRDistanceConstrain", "CVRFaceTracking", "CVRLeg",
            "CVRLineRendererHelper", "CVRMaterialDriver", "CVRMaterialUpdater", "CVRPointer",
            "CVRSkyboxManipulator", "CVRToggleStatePointer", "FABRIK", "FABRIKRoot", "FixedJoint",
            "FullBodyBipedIK", "GrounderBipedIK", "GrounderIK", "HingeJoint", "IKExecutionOrder",
            "LightProbeProxyVolume", "LimbIK", "LineRenderer", "LookAtConstraint", "MeshFilter",
            "MeshRenderer", "ParentConstraint", "PlayerMaterialParser", "PositionConstraint",
            "Rigidbody", "RotationConstraint", "RotationLimitAngle", "RotationLimitHinge",
            "RotationLimitPolygonal", "RotationLimitSpline", "ScaleConstraint", "Sensor",
            "SkinnedMeshRenderer", "Skybox", "SpringJoint", "TrailRenderer",
            // LocalComponentWhitelist — the wearer's copy only, but not destroyed outright.
            "CVRAdvancedAvatarSettingsTrigger", "CVRHapticAreaChest", "CVRParameterStream",
            "CVRSnappingPoint", "CVRToggleStateTrigger", "FPRExclusion",
            // Colliders, dynamics and dynamics colliders.
            "BoxCollider", "CapsuleCollider", "MeshCollider", "SphereCollider", "WheelCollider",
            "BaseCloth", "DynamicBone", "DynamicBoneCollider", "DynamicBoneColliderBase",
            "DynamicBonePlaneCollider", "MagicaBoneCloth", "MagicaBoneSpring", "MagicaMeshCloth",
            "MagicaMeshSpring", "MagicaRenderDeformer", "MagicaVirtualDeformer",
            // MagicaCloth 2, which the client spells out as MagicaCloth2.MagicaCloth and friends.
            // Matched here on the short name, like everything else in this set. Leaving these out
            // put a false "ChilloutVR will delete this" on every avatar converted down
            // AvatarBridge's primary physics path.
            "MagicaCloth", "ColliderComponent", "MagicaCapsuleCollider", "MagicaPlaneCollider",
            "MagicaSphereCollider", "RectTransform",
            // Renderers and particles.
            "ParticleSystem", "ParticleSystemForceField", "ParticleSystemRenderer",
            // Conditional on the viewer's own settings rather than on the avatar.
            "AudioSource", "SteamAudioSource", "AudioLowPassFilter", "AudioHighPassFilter",
            "AudioEchoFilter", "AudioDistortionFilter", "AudioReverbFilter", "AudioChorusFilter",
            "CVRParticleSound", "Camera", "CVRBlitter", "CVRBlitterController",
            "CVRRenderController", "CVRMaterialDataProvider", "Projector",
            "CVRTexturePropertyParser", "CVRCustomRenderTextureUpdater", "Light",
            "CVRMovementParent", "CVRAvatar",
        };

        /// <summary>
        /// Names the components ChilloutVR will delete the moment this avatar loads.
        /// </summary>
        /// <summary>
        /// States the avatar can enter and then never leave.
        ///
        /// A gesture that switches an expression on and never hands it back, a toggle that sticks:
        /// from the wearer's side it reads as "the animation played and got stuck", and it is
        /// invisible in the editor because entering the state works perfectly. What is broken is
        /// the way OUT.
        ///
        /// Reported only when a state HAS exit transitions and every one of them is
        /// unsatisfiable — someone plainly intended an exit and it cannot fire. A state with no
        /// outgoing transitions at all is deliberately terminal on plenty of avatars (a one-shot
        /// layer parked on its last pose), so flagging those would bury the real finding. The
        /// remote-thrash check earlier in this file fired on ten of fifty avatars before it was
        /// narrowed the same way; a detector nobody trusts is worse than none.
        ///
        /// Exit time is an escape by itself — a transition with hasExitTime leaves on the clock
        /// regardless of conditions — so any of those clears the state immediately.
        /// </summary>
        /// <summary>
        /// Test seam for <see cref="CheckStuckStates"/>. It returned clean on all fifty corpus
        /// avatars, which proves nothing on its own — a check that never fires looks identical to
        /// a healthy corpus. StuckStateDetectorTest drives known-answer controllers through this.
        /// </summary>
        internal static void RunStuckStateCheckForTest(BridgeContext ctx, AnimatorController master)
            => CheckStuckStates(ctx, master);

        static void CheckStuckStates(BridgeContext ctx, AnimatorController master)
        {
            var declared = new Dictionary<string, AnimatorControllerParameter>(StringComparer.Ordinal);
            foreach (var p in master.parameters)
            {
                declared[p.name] = p;
            }

            var stuck = new List<string>();
            foreach (var layer in master.layers)
            {
                if (layer == null || layer.stateMachine == null)
                {
                    continue;
                }
                var machines = new List<AnimatorStateMachine>();
                CollectMachines(layer.stateMachine, machines);

                foreach (var machine in machines)
                {
                    // AnyState reaches every state in its machine, so it is an escape route from
                    // all of them — except where it only targets the state we are standing in.
                    var anyEscapes = machine.anyStateTransitions ?? new AnimatorStateTransition[0];
                    foreach (var child in machine.states)
                    {
                        var state = child.state;
                        if (state == null)
                        {
                            continue;
                        }
                        var own = state.transitions ?? new AnimatorStateTransition[0];
                        if (own.Length == 0)
                        {
                            continue; // deliberately terminal, not a defect
                        }
                        bool escapable =
                            own.Any(t => t != null && (t.hasExitTime || CanEverFire(t, declared)))
                            || anyEscapes.Any(t => t != null && t.destinationState != state
                                                   && (t.hasExitTime || CanEverFire(t, declared)));
                        if (!escapable)
                        {
                            stuck.Add($"\"{layer.name}\" → \"{state.name}\"");
                        }
                    }
                }
            }

            if (stuck.Count == 0)
            {
                return;
            }
            ctx.Report.Warning("Animator",
                $"{stuck.Count} state(s) can be entered but never left",
                string.Join("; ", stuck.Take(8)) + (stuck.Count > 8 ? ", …" : "") +
                " — each of these has a transition out that can never fire, so whatever the state " +
                "switches on stays on for the rest of the session. The usual way to meet this is a " +
                "gesture or toggle that turns something on and won't turn it back off. Entering " +
                "works, which is why it looks fine in the editor: only the way out is broken. " +
                "Check the conditions on that state's outgoing transitions against the ones that " +
                "let it in — a band that lets you in on \"greater than\" and asks for \"greater " +
                "than\" again to leave is the common shape.");
        }

        /// <summary>
        /// Whether any parameter values could satisfy this transition at once. Contradictions
        /// within one transition are what make an exit dead: "Greater 3.9 AND Less 3.9" is
        /// enterable-looking and unsatisfiable.
        /// </summary>
        static bool CanEverFire(AnimatorStateTransition transition,
            Dictionary<string, AnimatorControllerParameter> declared)
        {
            var conditions = transition.conditions;
            if (conditions == null || conditions.Length == 0)
            {
                return true; // unconditional
            }
            foreach (var group in conditions.GroupBy(c => c.parameter, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(group.Key) || !declared.TryGetValue(group.Key, out var p))
                {
                    // Undeclared parameters are already reported by Validate; judging them here
                    // would double up and could only guess.
                    continue;
                }
                if (p.type == AnimatorControllerParameterType.Bool
                    || p.type == AnimatorControllerParameterType.Trigger)
                {
                    bool wantsTrue = group.Any(c => c.mode == AnimatorConditionMode.If);
                    bool wantsFalse = group.Any(c => c.mode == AnimatorConditionMode.IfNot);
                    if (wantsTrue && wantsFalse)
                    {
                        return false;
                    }
                    continue;
                }

                // Numeric: intersect every bound and see whether anything survives.
                float low = float.NegativeInfinity, high = float.PositiveInfinity;
                foreach (var c in group)
                {
                    switch (c.mode)
                    {
                        case AnimatorConditionMode.Greater: low = Mathf.Max(low, c.threshold); break;
                        case AnimatorConditionMode.Less: high = Mathf.Min(high, c.threshold); break;
                        case AnimatorConditionMode.Equals:
                            low = Mathf.Max(low, c.threshold);
                            high = Mathf.Min(high, c.threshold);
                            break;
                    }
                }
                if (low > high)
                {
                    return false;
                }
                // Greater/Less are strict, so a band that collapses to a single point is empty
                // unless an Equals put it there.
                if (Mathf.Approximately(low, high)
                    && !group.Any(c => c.mode == AnimatorConditionMode.Equals))
                {
                    return false;
                }
            }
            return true;
        }

        static void CollectMachines(AnimatorStateMachine machine, List<AnimatorStateMachine> into)
        {
            if (machine == null || into.Contains(machine))
            {
                return;
            }
            into.Add(machine);
            foreach (var child in machine.stateMachines)
            {
                CollectMachines(child.stateMachine, into);
            }
        }

        static void CheckComponentWhitelist(BridgeContext ctx)
        {
            var doomed = new Dictionary<string, int>();
            foreach (var component in ctx.Target.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue; // missing script; reported elsewhere
                }
                string name = component.GetType().Name;
                if (CvrAvatarComponentWhitelist.Contains(name))
                {
                    continue;
                }
                doomed[name] = doomed.TryGetValue(name, out var n) ? n + 1 : 1;
            }
            if (doomed.Count == 0)
            {
                return;
            }
            var listed = doomed.OrderByDescending(p => p.Value).Select(p => $"{p.Key} ×{p.Value}");
            ctx.Report.Error(Category, $"{doomed.Values.Sum()} component(s) ChilloutVR will delete on load",
                $"{Join(listed)} — ChilloutVR filters every component on an avatar against a fixed list and " +
                "destroys anything not on it. There is no warning in game and nothing looks wrong in the " +
                "editor; the component is simply gone once the avatar loads, along with whatever it did. " +
                "Worlds are allowed far more than avatars, so a component working in a ChilloutVR world says " +
                "nothing about whether it survives on one.");

            // The grounders deserve their own note. They are the FinalIK components most likely to
            // be on a converted avatar, the split between allowed and forbidden looks arbitrary,
            // and the obvious substitution does not work.
            var grounders = doomed.Keys.Where(n => n.StartsWith("Grounder")).ToList();
            if (grounders.Count > 0)
            {
                ctx.Report.Warning(Category, $"FinalIK grounding is lost ({Join(grounders)})",
                    "ChilloutVR permits VRIK, LookAtIK and TwistRelaxer on an avatar, and GrounderIK and " +
                    "GrounderBipedIK, but not GrounderVRIK, GrounderQuadruped, GrounderFBBIK or the Grounder " +
                    "base class. GrounderIK is NOT a drop-in replacement: GrounderVRIK works by adding " +
                    "position offsets into VRIK's own solver from inside its update callbacks, while " +
                    "GrounderIK drives separate per-leg IK components and never touches VRIK. Swapping them " +
                    "produces no grounding at all rather than different grounding. ChilloutVR has no native " +
                    "foot placement to fall back on either — its IK system only tracks whether the character " +
                    "controller is grounded. Feet will not adapt to terrain; VRIK's own locomotion still runs.");
            }
        }

        /// <summary>
        /// The four macros a shader needs to render correctly under single-pass instanced stereo,
        /// which is how both ChilloutVR and VRChat draw in VR. Taken from the CCK's own
        /// ShaderStereoSupportStep so this reports the same shaders its uploader will.
        /// </summary>
        static readonly string[] StereoMacros =
        {
            "UNITY_VERTEX_INPUT_INSTANCE_ID",
            "UNITY_VERTEX_OUTPUT_STEREO",
            "UNITY_SETUP_INSTANCE_ID",
            "UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO",
        };

        /// <summary>Shaders the CCK treats as known-good without scanning.</summary>
        static bool IsKnownStereoShader(string name) =>
            name == "Standard" || name.StartsWith("Hidden/PostProcessing/", StringComparison.Ordinal);

        /// <summary>
        /// Names shaders that will not draw correctly in VR.
        ///
        /// Single-pass instanced renders both eyes in one pass, and a shader has to opt in with
        /// four macros to know which eye it is drawing. Without them the effect typically appears
        /// in one eye only, or at the wrong offset.
        ///
        /// This *is* a ChilloutVR-specific problem, and an earlier version of this comment had it
        /// backwards. The two SDKs force different stereo modes, both unconditionally: the CCK
        /// sets `StereoRenderingPath.Instancing` (`CCK_EnvConfig.cs`), while the VRChat SDK sets
        /// `StereoRenderingPath.SinglePass` — the double-wide one (`EnvConfig.cs`). Under
        /// double-wide a shader gets both eyes without opting in, so one of these shaders looks
        /// correct in VRChat and its author had no reason to know. Converting is what exposes it.
        ///
        /// Worth catching before upload because of what happens if the avatar is ever treated as
        /// legacy content: NonSpiHelper replaces shaders by looking the name up in the game's own
        /// build, and CVRTools.ReplaceShaders falls back to "Standard" when the name isn't found.
        /// A particle shader nobody else ships would not merely render oddly, it would become an
        /// opaque surface.
        ///
        /// Same detection the CCK uses — a text scan of the shader source, following includes —
        /// so nothing is reported here that its uploader would pass, and vice versa. Shaders with
        /// no readable source (built-in, or inside a package) are skipped rather than guessed at.
        /// </summary>
        static void CheckStereoShaders(BridgeContext ctx)
        {
            var missingCache = new Dictionary<string, List<string>>();
            var offenders = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            foreach (var renderer in ctx.Target.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    var shader = material != null ? material.shader : null;
                    if (shader == null || IsKnownStereoShader(shader.name))
                    {
                        continue;
                    }
                    string path = AssetDatabase.GetAssetPath(shader);
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                        || !File.Exists(path))
                    {
                        continue; // no source to read; the CCK can't judge it either
                    }
                    if (!missingCache.TryGetValue(path, out var missing))
                    {
                        missingCache[path] = missing = MissingStereoMacros(path);
                    }
                    if (missing.Count == 0)
                    {
                        continue;
                    }
                    string key = $"{shader.name} [missing {string.Join(", ", missing)}]";
                    if (!offenders.TryGetValue(key, out var users))
                    {
                        offenders[key] = users = new SortedSet<string>(StringComparer.Ordinal);
                    }
                    users.Add(material.name);
                }
            }

            if (offenders.Count == 0)
            {
                return;
            }
            var listed = offenders.Select(kv => $"{kv.Key} ({Join(kv.Value, 4)})");
            ctx.Report.Warning(Category, $"{offenders.Count} shader(s) may not render correctly in VR",
                // Short on purpose. This used to carry a paragraph of hand-editing instructions
                // that only an HLSL author could use, in front of everyone who just wanted to
                // know what to do — so nobody read any of it. The edit itself lives in the
                // README now; the report says what breaks and what to press.
                $"{Join(listed, 6)} — these draw into ONE EYE ONLY under ChilloutVR's rendering mode. " +
                "It looked fine in VRChat because VRChat's mode hands a shader both eyes without it asking, " +
                "so expect this to be new. " +
                (ctx.Settings.patchNonSpiShaders
                    ? "AvatarBridge tried to fix them and the entry above says why it couldn't."
                    : "Turn on \"Patch non-SPI shaders for VR\" in Advanced and convert again — it patches a " +
                      "copy and checks it compiles.") +
                " Otherwise swap the shader, or accept how it looks; the README has the hand-edit.");
        }

        /// <summary>
        /// Which of the four macros the shader never mentions, following includes.
        ///
        /// Naming them turns "review this for compatibility" into something actionable: each one
        /// belongs in a specific place, so the list doubles as the edit needed —
        /// UNITY_VERTEX_INPUT_INSTANCE_ID in the vertex input struct,
        /// UNITY_VERTEX_OUTPUT_STEREO in the interpolator struct, and
        /// UNITY_SETUP_INSTANCE_ID plus UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO at the top of the
        /// vertex function.
        /// </summary>
        static List<string> MissingStereoMacros(string path)
        {
            var remaining = new HashSet<string>(StereoMacros, StringComparer.Ordinal);
            Scan(path, remaining, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
            return StereoMacros.Where(remaining.Contains).ToList();
        }

        static void Scan(string path, HashSet<string> remaining, HashSet<string> seen, int depth)
        {
            if (depth > 8 || remaining.Count == 0 || !seen.Add(path) || !File.Exists(path))
            {
                return;
            }
            var includes = new List<string>();
            string dir = Path.GetDirectoryName(path) ?? "";
            foreach (var line in File.ReadLines(path))
            {
                remaining.RemoveWhere(m => line.Contains(m, StringComparison.Ordinal));
                if (remaining.Count == 0)
                {
                    return;
                }
                int hash = line.IndexOf("#include", StringComparison.Ordinal);
                if (hash < 0)
                {
                    continue;
                }
                int open = line.IndexOf('"', hash);
                int close = open >= 0 ? line.IndexOf('"', open + 1) : -1;
                if (close > open)
                {
                    includes.Add(Path.GetFullPath(Path.Combine(dir, line.Substring(open + 1, close - open - 1))));
                }
            }
            foreach (var include in includes)
            {
                Scan(include, remaining, seen, depth + 1);
            }
        }

        // ChilloutVR's own sync budget, from AvatarAnimatorManager.CreateParameterDefinition.
        const int AasBitBudget = 3200;

        /// <summary>
        /// AvatarDefinitions.CoreParameters, exactly as the client spells it.
        ///
        /// Deliberately not reusing CckBaseParameters or AnimatorMerger.CvrCoreParameters: both
        /// carry Swimming and AFK, which the client writes but does NOT mark core. That makes
        /// them writable and — the part that matters here — they DO consume sync bits. Counting
        /// with the wrong set silently under-reports the budget.
        /// </summary>
        static readonly HashSet<string> ClientCoreParameters = new HashSet<string>
        {
            "MovementX", "MovementY", "Grounded", "Crouching", "Prone", "Flying", "Sitting",
            "GestureRight", "GestureLeft", "Toggle", "Emote", "CancelEmote", "IsLocal",
            "GestureLeftIdx", "GestureRightIdx", "DistanceTo", "VisemeIdx", "VisemeLoudness",
            "IsFriend", "VelocityX", "VelocityY", "VelocityZ"
        };

        /// <summary>
        /// Reproduces ChilloutVR's sync-slot allocation so the report can say when parameters
        /// fall off the end of it.
        ///
        /// A parameter syncs when it is not "#"-prefixed, not a Trigger, and not one of the
        /// client's core parameters — a menu entry has nothing to do with it. Slots are handed
        /// out walking the animator's parameter list in declaration order, and the budget is
        /// tested BEFORE each one is admitted, so the parameter that crosses the line still gets
        /// in and everything after it is dropped. Nothing warns about this in game: the
        /// parameters simply never replicate.
        ///
        /// Costs are the client's: Float and Int 32 bits each, Bool 1.
        ///
        /// The client also exempts parameters an animation curve drives. That is a runtime test
        /// with no static equivalent, and skipping it can only make this estimate too high, which
        /// is the safe direction for a budget warning.
        /// </summary>
        static void CheckSyncBudget(BridgeContext ctx, AnimatorController master)
        {
            int used = 0;
            int floats = 0, ints = 0, bools = 0;
            var dropped = new List<string>();

            foreach (var param in master.parameters)
            {
                if (param.name.StartsWith("#")
                    || param.type == AnimatorControllerParameterType.Trigger
                    || ClientCoreParameters.Contains(param.name))
                {
                    continue;
                }
                if (used >= AasBitBudget)
                {
                    dropped.Add(param.name);
                    continue;
                }
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float: floats++; used += 32; break;
                    case AnimatorControllerParameterType.Int: ints++; used += 32; break;
                    case AnimatorControllerParameterType.Bool: bools++; used += 1; break;
                }
            }

            if (dropped.Count > 0)
            {
                ctx.Report.Error(Category, $"{dropped.Count} parameter(s) past ChilloutVR's sync limit",
                    $"{Join(dropped)} — the avatar needs more than ChilloutVR's {AasBitBudget} sync bits " +
                    $"({floats} floats and {ints} ints at 32 bits each, {bools} bools at 1). Slots are handed " +
                    "out in the order the animator declares its parameters, and these came too late to get " +
                    "one. They will work for the wearer and never replicate to anyone else, with no warning " +
                    "in game. Make the ones that don't need to replicate local by prefixing them with \"#\", " +
                    "or convert floats you only use as on/off into bools — a bool costs 1 bit instead of 32.");
            }
            else if (used > AasBitBudget * 3 / 4)
            {
                ctx.Report.Warning(Category, $"Sync budget {used}/{AasBitBudget} bits used",
                    $"{floats} floats and {ints} ints at 32 bits each, {bools} bools at 1. Parameters added " +
                    "beyond the limit stop replicating silently, so there is not much headroom left.");
            }
        }

        static readonly HashSet<string> CckBaseParameters = new HashSet<string>
        {
            "MovementX", "MovementY", "Grounded", "Emote", "CancelEmote",
            "GestureLeft", "GestureRight", "GestureLeftIdx", "GestureRightIdx",
            "Toggle", "Sitting", "Crouching", "Prone", "Flying", "Swimming",
            "IsLocal", "VisemeIdx", "VisemeLoudness"
        };

        static bool IsPlatformDriven(string name)
        {
            string bare = name.TrimStart('#');
#if VRC_SDK_VRCSDK3
            if (AnimatorMerger.IsGameDrivenParameter(bare))
            {
                return true;
            }
#endif
            return CckBaseParameters.Contains(bare);
        }

        /// <summary>Comma-joined, capped so one runaway list can't bury the rest of the report.</summary>
        static string Join(IEnumerable<string> items, int max = 12)
        {
            var list = items.ToList();
            return list.Count <= max
                ? string.Join(", ", list)
                : string.Join(", ", list.Take(max)) + $", … (+{list.Count - max} more)";
        }
    }
}
#endif
