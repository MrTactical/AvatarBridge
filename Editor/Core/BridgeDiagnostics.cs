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
    // Runs last, and does two things the rest of the converter doesn't.
    //
    // **Validates.** Every check here is for something that breaks
    // silently rather than erroring. CVR does not complain about any
    // of these; it quietly does nothing.
    //
    // **Dumps.** Every bug diagnosed on this project so far started with someone grepping the
    // generated .controller by hand: does anything read this parameter, what compares it, what
    // do the drivers write. Those answers now go into the report itself, so a bug report is
    // answerable without a round trip asking for the controller.
    public static class BridgeDiagnostics
    {
        const string Category = "Diagnostics";

        // How a parameter is touched, which is the question that keeps coming up.
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
            // Parameters CVR drives itself are excluded; flagging those
            // fires on every conversion, and a check that always fires
            // is a check people learn to scroll past.
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
                          $"bound swing: `{s.boundSwingToSourceLimit}` · mesh radius: `{s.fitRadiusToMesh}` · " +
                          $"auto colliders: `{s.autoAssignNearbyColliders}`");
            sb.AppendLine($"- strip GoGo: `{s.stripGogoLoco}` · strip SPS: `{s.stripSpsSystems}` · " +
                          $"convert YAPS: `{s.convertYapsSystems}` · " +
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
            // Muscle curves bind like animated animator parameters.
            // Requiring a declared name separates the two; an
            // undeclared parameter write does nothing anyway.
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
                    // Count only the axis fields this blend type reads.
                    // Direct trees read neither, 1D trees only X;
                    // leftover names would read as live references.
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
                // IsLocal has an answer rather than an unknown: on a
                // remote copy it is false by definition. Reading its
                // declared default would invert every local/remote gate.
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

                // A cycle only matters if the layer can reach it at
                // these values. VRCFury parks layers in a Remote Trap
                // state on remotes; without a reachability check the
                // defence reads as the bug.
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
                    // Depth-first cycle search. A cycle only counts with
                    // at least one instant edge; otherwise the layer is
                    // a sequence, not a thrash.
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

        static readonly HashSet<string> CvrAvatarComponentWhitelist = new HashSet<string>
        {
            // RootComponents: permitted anywhere, despite the name.
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
            // LocalComponentWhitelist: the wearer's copy only, but not destroyed outright.
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
                    // AnyState is an escape route from every state,
                    // except where it only targets the current one.
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
            var listed = doomed.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} ×{p.Value}");
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

        static readonly string[] StereoMacros =
        {
            "UNITY_VERTEX_INPUT_INSTANCE_ID",
            "UNITY_VERTEX_OUTPUT_STEREO",
            "UNITY_SETUP_INSTANCE_ID",
            "UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO",
        };

        static bool IsKnownStereoShader(string name) =>
            name == "Standard" || name.StartsWith("Hidden/PostProcessing/", StringComparison.Ordinal);

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
                // Short on purpose. The hand-edit lives in the README;
                // the report says what breaks and what to press.
                $"{Join(listed, 6)} — these draw into ONE EYE ONLY under ChilloutVR's rendering mode. " +
                "It looked fine in VRChat because VRChat's mode hands a shader both eyes without it asking, " +
                "so expect this to be new. " +
                (ctx.Settings.patchNonSpiShaders
                    ? "AvatarBridge tried to fix them and the entry above says why it couldn't."
                    : "Turn on \"Patch non-SPI shaders for VR\" in Advanced and convert again — it patches a " +
                      "copy and checks it compiles.") +
                " Otherwise swap the shader, or accept how it looks; the README has the hand-edit.");
        }

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

        static readonly HashSet<string> ClientCoreParameters = new HashSet<string>
        {
            "MovementX", "MovementY", "Grounded", "Crouching", "Prone", "Flying", "Sitting",
            "GestureRight", "GestureLeft", "Toggle", "Emote", "CancelEmote", "IsLocal",
            "GestureLeftIdx", "GestureRightIdx", "DistanceTo", "VisemeIdx", "VisemeLoudness",
            "IsFriend", "VelocityX", "VelocityY", "VelocityZ"
        };

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
