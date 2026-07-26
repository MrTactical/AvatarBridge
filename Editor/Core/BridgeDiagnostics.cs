#if CVR_CCK_EXISTS
using System.Collections.Generic;
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
                    For(tree.blendParameter).BlendTrees++;
                    For(tree.blendParameterY).BlendTrees++;
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
