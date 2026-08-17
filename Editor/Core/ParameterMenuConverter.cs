#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ABI.CCK.Components;
using ABI.CCK.Scripts;

namespace AvatarBridge
{
    // Converts VRChat expression parameters + expression menus into ChilloutVR
    // Advanced Avatar Settings entries:
    //
    //   Bool  + menu toggle/button  -> Toggle
    //   Int   + menu toggles        -> Dropdown (one option per used value) or Toggle
    //   Float + radial/puppet menu  -> Slider
    //
    // The generated entries deliberately have no GameObject targets: the converted FX
    // layers already react to the parameters, the entries only exist so CVR shows menu
    // controls and syncs the values.
    public static class ParameterMenuConverter
    {
        const string Category = "Parameters & menu";

        static readonly (string Needle, string Parameter, string When)[] ClientDrivenEquivalents =
        {
            ("flight", "Flying", "you actually fly (or enter a zero-gravity world)"),
            ("flying", "Flying", "you actually fly (or enter a zero-gravity world)"),
            ("swimming", "Swimming", "you enter water"),
            ("crouch", "Crouching", "you crouch"),
            ("prone", "Prone", "you go prone"),
        };

        static void NoteClientDrivenEquivalent(BridgeContext ctx, string parameterName, string display)
        {
            string haystack = ((parameterName ?? "") + " " + (display ?? "")).ToLowerInvariant();
            foreach (var candidate in ClientDrivenEquivalents)
            {
                if (haystack.IndexOf(candidate.Needle, StringComparison.Ordinal) < 0)
                {
                    continue;
                }
                ctx.Report.Converted(Category, $"\"{display}\" — ChilloutVR drives \"{candidate.Parameter}\" itself",
                    $"Converted as a normal menu toggle, which is what it was. Worth knowing: ChilloutVR " +
                    $"feeds every avatar a \"{candidate.Parameter}\" parameter and sets it whenever " +
                    $"{candidate.When} — the same way it drives Grounded and the movement velocities. " +
                    $"Point this layer's transitions at \"{candidate.Parameter}\" instead of the menu " +
                    "parameter and the pose follows what you're really doing, with no menu needed. " +
                    "The parameter is already declared and needs no parameter stream. Keep the menu " +
                    "toggle alongside it if you like: the client only sets these when the WORLD allows " +
                    "it, so in a world with flight disabled the automatic version never fires. Nothing " +
                    "was rewired here — which layer meant what is your call.");
                return;
            }
        }

        public const string UnusedOption = "(unused)";

        class MenuUse
        {
            public string DisplayName;
            public float Value;
            public VRCExpressionsMenu.Control.ControlType Type;
        }

        // A VRChat two-axis puppet: one control, two -1..1 parameters.
        class PuppetPair
        {
            public string Display;
            public string X;
            public string Y;
        }

        public static void Run(BridgeContext ctx)
        {
            var vrc = ctx.SourceDescriptor;
            var vrcParams = vrc.expressionParameters;

            // Which parameter names must keep their exact (synced) name.
            if (vrcParams != null && vrcParams.parameters != null)
            {
                foreach (var p in vrcParams.parameters)
                {
                    if (string.IsNullOrEmpty(p.name))
                    {
                        continue;
                    }
                    if (!ctx.Settings.preserveParameterSyncState || p.networkSynced)
                    {
                        ctx.PreserveParameters.Add(p.name);
                    }
                }
            }

            var uses = new Dictionary<string, List<MenuUse>>();
            var visited = new HashSet<VRCExpressionsMenu>();
            var puppets = new List<PuppetPair>();
            WalkMenu(vrc.expressionsMenu, "", uses, visited, ctx, puppets);

            // Outward-facing beats the flag. VRChat's 256-bit budget
            // taught authors and VRCFury to de-sync menu parameters and
            // carry them through side channels that do not survive
            // conversion, so "not synced" is untrustworthy on anything
            // a menu drives. A control whose effect nobody else can see
            // is a broken feature, and ChilloutVR's budget is 3200
            // bits: menu-driven parameters sync here regardless.
            if (ctx.Settings.preserveParameterSyncState
                && vrcParams != null && vrcParams.parameters != null)
            {
                var resynced = new List<string>();
                foreach (var p in vrcParams.parameters)
                {
                    if (!string.IsNullOrEmpty(p.name) && uses.ContainsKey(p.name)
                        && !ctx.PreserveParameters.Contains(p.name))
                    {
                        ctx.PreserveParameters.Add(p.name);
                        resynced.Add(p.name);
                    }
                }
                if (resynced.Count > 0)
                {
                    ctx.Report.Converted(Category,
                        $"{resynced.Count} menu parameter(s) synced although VRChat marked them local",
                        string.Join(", ", resynced.Take(12)) + (resynced.Count > 12 ? ", …" : "") +
                        " — every one of these is driven by a menu control, and a control whose " +
                        "effect other players cannot see is a broken feature. VRChat's tight sync " +
                        "budget made de-syncing menu parameters a common trick, usually with " +
                        "VRCFury syncing them through machinery that does not survive conversion. " +
                        "ChilloutVR's budget is 3200 bits, so they simply sync. Parameters no menu " +
                        "drives keep their imported local/synced state.");
                }
            }

            var entries = new List<CVRAdvancedSettingsEntry>();
            var usedNames = new HashSet<string>();

            // Two-axis puppets become one ChilloutVR Joystick2D rather than two sliders, so the
            // parameters they own must not also be built as sliders below.
            var joystickParams = new HashSet<string>();
            foreach (var entry in BuildJoysticks(ctx, puppets, usedNames, joystickParams))
            {
                entries.Add(entry);
            }

            // Menu entries show the leaf name ("Cloak"), qualified by their parent menu
            // only when several controls share the same leaf ("Hoodie (Tops)").
            var leafCounts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var pair in uses)
            {
                string leaf = ShortName(pair.Value[0].DisplayName);
                leafCounts[leaf] = leafCounts.TryGetValue(leaf, out var n) ? n + 1 : 1;
            }

            int ftSkipped = 0;
            if (vrcParams != null && vrcParams.parameters != null)
            {
                foreach (var p in vrcParams.parameters)
                {
                    if (string.IsNullOrEmpty(p.name))
                    {
                        continue;
                    }
                    // Face-tracking parameters are driven by the FT animator layers; never
                    // expose them as menu toggles.
                    if (FaceTrackingParameters.IsFaceTracking(p.name))
                    {
                        ftSkipped++;
                        continue;
                    }
                    if (joystickParams.Contains(p.name))
                    {
                        continue; // already covered by its Joystick2D entry
                    }
                    // Never build a control for a parameter the stripper is about to take. The
                    // joystick path has refused these since 2.32.3; the slider/toggle/dropdown
                    // path did not, so a stripped system's menu. GoGo's decoratively-labelled
                    // "- (-)" controls and its 256-value emote dropdown; was faithfully rebuilt
                    // as Advanced Avatar Settings and then left dangling once the layers reading
                    // it were removed.
                    if (SystemStripper.WillBeStripped(ctx, p.name))
                    {
                        ctx.Report.Skipped(Category, $"Menu control for \"{p.name}\" not created",
                            "The parameter belongs to a system being stripped (GoGo Loco / SPS / " +
                            "extra keywords), so a control for it would sit in the menu driving " +
                            "removed layers.");
                        continue;
                    }
                    var entry = BuildEntry(ctx, p, uses.TryGetValue(p.name, out var paramUses) ? paramUses : null, usedNames, leafCounts);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            else
            {
                ctx.Report.Warning(Category, "Expression parameters", "Avatar has no expression parameters asset.");
            }

            // Keep the CVR menu in roughly the same order as the VRChat menu.
            entries = entries.OrderBy(e =>
            {
                int index = ctx.ParameterOrder.IndexOf(e.machineName);
                return index == -1 ? int.MaxValue : index;
            }).ToList();

            if (ftSkipped > 0)
            {
                ctx.Report.Converted(Category, $"{ftSkipped} face-tracking parameter(s) left as-is",
                    "Not exposed as menu toggles — the FT animator layers drive them.");
            }

            ctx.CvrAvatar.avatarSettings.settings = entries;
            UnityEditor.EditorUtility.SetDirty(ctx.CvrAvatar);
            ctx.Report.Converted(Category, $"{entries.Count} Advanced Avatar Settings entries created");
            NoteParameterPackers(ctx, entries);
        }

        static List<CVRAdvancedSettingsEntry> BuildJoysticks(BridgeContext ctx, List<PuppetPair> puppets,
            HashSet<string> usedNames, HashSet<string> claimed)
        {
            var result = new List<CVRAdvancedSettingsEntry>();
            foreach (var puppet in puppets)
            {
                // A parameter can only belong to one control; if two puppets share an axis the
                // avatar is already ambiguous and the sliders are the safer reading.
                if (claimed.Contains(puppet.X) || claimed.Contains(puppet.Y))
                {
                    continue;
                }

                // Leave anything the stripper is about to take. GoGo Loco ships a puppet on
                // "Go/PuppetX"/"Go/PuppetY", and renaming those to a joystick's "-x"/"-y" moved
                // them out of the "Go/" family the stripper recognises; so GoGo's puppet
                // survived removal, wearing a joystick and a meaningless generated name.
                if (SystemStripper.WillBeStripped(ctx, puppet.X) ||
                    SystemStripper.WillBeStripped(ctx, puppet.Y))
                {
                    continue;
                }

                string label = ShortName(puppet.Display);
                string machine = AnimatorMerger.SanitizeParameterName(label);
                // An unnamed control gives the sanitizer nothing to work with and it falls back to
                // a generic name. A menu full of "Param" helps nobody; leave those as sliders.
                if (string.IsNullOrWhiteSpace(label) || string.IsNullOrEmpty(machine) ||
                    machine == "Param")
                {
                    continue;
                }
                string candidate = machine;
                for (int n = 2; !usedNames.Add(candidate); n++)
                {
                    candidate = machine + n;
                }

                ctx.ForcedRenames[puppet.X] = candidate + "-x";
                ctx.ForcedRenames[puppet.Y] = candidate + "-y";
                claimed.Add(puppet.X);
                claimed.Add(puppet.Y);

                // Both axes keep syncing: the pair is one control to the wearer, and half a
                // joystick arriving at other players is worse than none.
                ctx.PreserveParameters.Add(candidate + "-x");
                ctx.PreserveParameters.Add(candidate + "-y");
                // And the base name, which is not a parameter at all but is what ChilloutVR
                // derives the two axes from. Left out, the rename pass sees a name it doesn't
                // recognise as synced and prefixes the entry to "#TailController"; which then
                // derives "#TailController-x", matching nothing, and the dead-entry sweep removes
                // the control as unused. The axes were preserved; the name they hang off wasn't.
                ctx.PreserveParameters.Add(candidate);

                result.Add(new CVRAdvancedSettingsEntry
                {
                    name = label,
                    machineName = candidate,
                    unlinkNameFromMachineName = true,
                    type = CVRAdvancedSettingsEntry.SettingsType.Joystick2D,
                    setting = new CVRAdvancesAvatarSettingJoystick2D
                    {
                        // A VRChat puppet rests centred and reaches a full unit in each
                        // direction; ChilloutVR's default 0..1 range would clip half of it away.
                        defaultValue = Vector2.zero,
                        rangeMin = new Vector2(-1f, -1f),
                        rangeMax = new Vector2(1f, 1f),
                        usedType = CVRAdvancesAvatarSettingBase.ParameterType.Float,
                    }
                });

                ctx.Report.Converted(Category, label,
                    $"Two-axis puppet -> Joystick2D \"{candidate}\", full -1..1 on both axes. " +
                    $"\"{puppet.X}\" and \"{puppet.Y}\" are renamed to \"{candidate}-x\" and " +
                    $"\"{candidate}-y\", which is how ChilloutVR addresses a joystick's axes.");
            }
            return result;
        }

        static readonly string[] PackerPrefixes = { "MemOpt", "VFUP_", "VRCFuryUnlimited" };

        static void NoteParameterPackers(BridgeContext ctx, List<CVRAdvancedSettingsEntry> entries)
        {
            var packed = entries
                .Where(e => PackerPrefixes.Any(p => (e.machineName ?? "").StartsWith(p)))
                .Select(e => e.machineName)
                .ToList();
            if (packed.Count == 0)
            {
                return;
            }
            ctx.Report.Warning(Category, $"{packed.Count} packed sync slot(s) must stay in the menu",
                $"{string.Join(", ", packed.Take(4))}{(packed.Count > 4 ? ", …" : "")} — these belong to a " +
                "parameter-packing optimiser. The avatar's real toggles are local, and these slots are what " +
                "actually carries them to other players; animator drivers unpack them on arrival. They look " +
                "like meaningless menu entries because ChilloutVR can't sync a parameter without one. Leave " +
                "them alone — deleting them gives you an avatar whose toggles work on your screen and never " +
                "change on anyone else's.");
        }

        static CVRAdvancedSettingsEntry BuildEntry(BridgeContext ctx, VRCExpressionParameters.Parameter p,
            List<MenuUse> paramUses, HashSet<string> usedNames, Dictionary<string, int> leafCounts)
        {
            bool hasMenu = paramUses != null && paramUses.Count > 0;

            // ChilloutVR supplies some values itself; gestures, locomotion, visemes, and the
            // stream-fed trigger/mute/VR-mode parameters. Avatars commonly declare those as
            // synced so their FX can read them, but a menu control for one is worse than
            // useless: the game overwrites it every frame, so the control does nothing while
            // still occupying menu space and sync budget.
            if (CvrParameterNames.IsGameDriven(p.name))
            {
                ctx.Report.Skipped(Category, p.name,
                    "No menu control created — ChilloutVR drives this parameter itself, so the avatar's " +
                    "animator still reads it, but a menu entry would only fight the game for the value.");
                return null;
            }

            if (!hasMenu && !ctx.Settings.exposeMenulessSyncedParameters)
            {
                ctx.Report.Skipped(Category, p.name, "Not referenced by any menu control.");
                return null;
            }
            if (!hasMenu && ctx.Settings.preserveParameterSyncState && !p.networkSynced)
            {
                // Local-only and menuless: nothing to expose, the animator keeps it internal.
                return null;
            }

            if (!hasMenu)
            {
                // Remembered, not decided here: whether this one is worth keeping depends on what
                // the merged animator turns out to do with the parameter, and the merge hasn't
                // happened yet. See AnimatorMerger.WithdrawSelfDrivenExposures.
                ctx.AutoExposedParameters.Add(p.name);
            }

            string display = hasMenu
                ? FriendlyMenuName(paramUses[0].DisplayName, leafCounts)
                : FriendlyParamName(p.name);
            display = MakeUnique(display, usedNames);

            // VRChat menu Buttons are momentary; held while pressed, then reset. ChilloutVR's
            // menu has no equivalent control type (Toggle/Dropdown/Color/Slider/Input... only), so
            // they convert as ordinary toggles: the value stays until switched off.
            if (hasMenu && paramUses.All(u => u.Type == VRCExpressionsMenu.Control.ControlType.Button))
            {
                ctx.ImpulseParameters.Add(p.name);
                ctx.Report.Approximated(Category, p.name,
                    $"\"{display}\" was a momentary Button in VRChat; ChilloutVR has no button control, " +
                    "so it becomes a toggle you switch off again. If it drives a one-shot effect, add a " +
                    "state in the animator that drives the parameter back to 0.");
            }

            NoteClientDrivenEquivalent(ctx, p.name, display);

            switch (p.valueType)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    ctx.Report.Converted(Category, p.name, $"Toggle \"{display}\"");
                    return new CVRAdvancedSettingsEntry
                    {
                        name = display,
                        machineName = p.name,
                        unlinkNameFromMachineName = true,
                        setting = new CVRAdvancesAvatarSettingGameObjectToggle
                        {
                            defaultValue = p.defaultValue != 0,
                            usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool
                        }
                    };

                case VRCExpressionParameters.ValueType.Float:
                    // VRCFury bakes most toggles as float parameters driven by blend
                    // trees. When every menu use is a toggle/button, expose a checkbox
                    // (writing 0/1 into the float) instead of a slider.
                    if (hasMenu && paramUses.All(u =>
                            u.Type == VRCExpressionsMenu.Control.ControlType.Toggle ||
                            u.Type == VRCExpressionsMenu.Control.ControlType.Button))
                    {
                        ctx.Report.Converted(Category, p.name, $"Toggle \"{display}\" (float parameter)");
                        return new CVRAdvancedSettingsEntry
                        {
                            name = display,
                            machineName = p.name,
                            unlinkNameFromMachineName = true,
                            setting = new CVRAdvancesAvatarSettingGameObjectToggle
                            {
                                defaultValue = p.defaultValue != 0,
                                usedType = CVRAdvancesAvatarSettingBase.ParameterType.Float
                            }
                        };
                    }
                    if (hasMenu && paramUses.Any(u =>
                        u.Type == VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet))
                    {
                        ctx.Report.Approximated(Category, p.name,
                            "Two-axis puppet axis converted to a 0..1 slider; the -1..0 half is unreachable from the CVR menu.");
                    }
                    else
                    {
                        ctx.Report.Converted(Category, p.name, $"Slider \"{display}\"");
                    }
                    return new CVRAdvancedSettingsEntry
                    {
                        name = display,
                        machineName = p.name,
                        unlinkNameFromMachineName = true,
                        type = CVRAdvancedSettingsEntry.SettingsType.Slider,
                        setting = new CVRAdvancesAvatarSettingSlider
                        {
                            defaultValue = p.defaultValue,
                            usedType = CVRAdvancesAvatarSettingBase.ParameterType.Float
                        }
                    };

                case VRCExpressionParameters.ValueType.Int:
                    return BuildIntEntry(ctx, p, paramUses, display);

                default:
                    ctx.Report.Skipped(Category, p.name, $"Unknown parameter type {p.valueType}.");
                    return null;
            }
        }

        static CVRAdvancedSettingsEntry BuildIntEntry(BridgeContext ctx, VRCExpressionParameters.Parameter p,
            List<MenuUse> paramUses, string display)
        {
            bool hasMenu = paramUses != null && paramUses.Count > 0;

            // A single menu toggle that sets the int to 1 behaves like a bool.
            if (hasMenu && paramUses.Count == 1 && Mathf.Approximately(paramUses[0].Value, 1f))
            {
                ctx.Report.Converted(Category, p.name, $"Toggle \"{display}\" (int used as bool)");
                return new CVRAdvancedSettingsEntry
                {
                    name = display,
                    machineName = p.name,
                    unlinkNameFromMachineName = true,
                    setting = new CVRAdvancesAvatarSettingGameObjectToggle
                    {
                        defaultValue = Mathf.Approximately(p.defaultValue, 1f),
                        usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool
                    }
                };
            }

            // What the animator actually reacts to, and the state name behind each value.
            var animatorValues = ScanIntUsage(ctx.SourceDescriptor, p.name);

            var valueNames = new Dictionary<int, string>();
            if (hasMenu)
            {
                foreach (var use in paramUses)
                {
                    valueNames[(int)use.Value] = ShortName(use.DisplayName);
                }
            }
            else
            {
                foreach (var kv in animatorValues)
                {
                    valueNames[kv.Key] = kv.Value ?? (p.name + " = " + kv.Key);
                }
            }
            int maxValue = Mathf.Max(valueNames.Count > 0 ? valueNames.Keys.Max() : 0, (int)p.defaultValue);
            maxValue = Mathf.Min(maxValue, 255);

            // ChilloutVR dropdowns map option INDEX to the parameter value, so reaching value
            // N needs N+1 entries here. AnimatorMerger.CompactIntDropdowns usually renumbers
            // the parameter afterwards and removes the gaps entirely; this fills them as
            // usefully as possible for the cases where it can't: a value the animator responds
            // to gets its state's name (a working option the VRChat menu never exposed), and
            // one nothing reacts to is labelled as such rather than an ambiguous "---".
            var options = new List<CVRAdvancedSettingsDropDownEntry>();
            int live = 0, inert = 0;
            for (int i = 0; i <= maxValue; i++)
            {
                string label;
                if (valueNames.TryGetValue(i, out var menuLabel))
                {
                    label = menuLabel;
                    live++;
                }
                else if (animatorValues.TryGetValue(i, out var stateName))
                {
                    label = stateName ?? $"Option {i}";
                    live++;
                }
                else
                {
                    label = UnusedOption;
                    inert++;
                }
                options.Add(new CVRAdvancedSettingsDropDownEntry { name = label });
            }

            // Gaps are reported later: the animator merge renumbers the parameter to remove
            // them where it can, and only explains them when it can't (CompactIntDropdowns).
            ctx.Report.Converted(Category, p.name,
                inert > 0
                    ? $"Dropdown \"{display}\" with {options.Count} options ({live} mapped to a value the avatar uses)"
                    : $"Dropdown \"{display}\" with {options.Count} options");
            return new CVRAdvancedSettingsEntry
            {
                name = display,
                machineName = p.name,
                unlinkNameFromMachineName = true,
                type = CVRAdvancedSettingsEntry.SettingsType.Dropdown,
                setting = new CVRAdvancesAvatarSettingGameObjectDropdown
                {
                    defaultValue = (int)p.defaultValue,
                    options = options,
                    usedType = CVRAdvancesAvatarSettingBase.ParameterType.Int
                }
            };
        }

        static void WalkMenu(VRCExpressionsMenu menu, string prefix,
            Dictionary<string, List<MenuUse>> uses, HashSet<VRCExpressionsMenu> visited, BridgeContext ctx,
            List<PuppetPair> puppets)
        {
            if (menu == null || visited.Contains(menu))
            {
                return;
            }
            visited.Add(menu);

            foreach (var control in menu.controls)
            {
                string display = prefix + YapsLabel(ctx, CleanMenuName(control.name));
                switch (control.type)
                {
                    case VRCExpressionsMenu.Control.ControlType.Toggle:
                    case VRCExpressionsMenu.Control.ControlType.Button:
                        RecordUse(uses, ctx, control.parameter?.name, display, control.value, control.type);
                        break;

                    case VRCExpressionsMenu.Control.ControlType.RadialPuppet:
                        RecordSubParameter(uses, ctx, control, 0, display, control.type);
                        break;

                    case VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet:
                        RecordSubParameter(uses, ctx, control, 0, display + " (Horizontal)", control.type);
                        RecordSubParameter(uses, ctx, control, 1, display + " (Vertical)", control.type);
                        string px = SubParameterName(control, 0), py = SubParameterName(control, 1);
                        if (!string.IsNullOrEmpty(px) && !string.IsNullOrEmpty(py) && px != py)
                        {
                            puppets.Add(new PuppetPair { Display = display, X = px, Y = py });
                        }
                        break;

                    case VRCExpressionsMenu.Control.ControlType.FourAxisPuppet:
                        RecordSubParameter(uses, ctx, control, 0, display + " (Up)", control.type);
                        RecordSubParameter(uses, ctx, control, 1, display + " (Right)", control.type);
                        RecordSubParameter(uses, ctx, control, 2, display + " (Down)", control.type);
                        RecordSubParameter(uses, ctx, control, 3, display + " (Left)", control.type);
                        break;

                    case VRCExpressionsMenu.Control.ControlType.SubMenu:
                        WalkMenu(control.subMenu, display + "/", uses, visited, ctx, puppets);
                        break;
                }
            }
        }

        static string SubParameterName(VRCExpressionsMenu.Control control, int index) =>
            control.subParameters != null && control.subParameters.Length > index
                ? control.subParameters[index]?.name
                : null;

        static void RecordSubParameter(Dictionary<string, List<MenuUse>> uses, BridgeContext ctx,
            VRCExpressionsMenu.Control control, int index, string display,
            VRCExpressionsMenu.Control.ControlType type)
        {
            if (control.subParameters != null && control.subParameters.Length > index)
            {
                RecordUse(uses, ctx, control.subParameters[index]?.name, display, 0f, type);
            }
        }

        static void RecordUse(Dictionary<string, List<MenuUse>> uses, BridgeContext ctx,
            string parameterName, string display, float value, VRCExpressionsMenu.Control.ControlType type)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                return;
            }
            if (!uses.TryGetValue(parameterName, out var list))
            {
                uses[parameterName] = list = new List<MenuUse>();
            }
            list.Add(new MenuUse { DisplayName = display, Value = value, Type = type });
            if (!ctx.ParameterOrder.Contains(parameterName))
            {
                ctx.ParameterOrder.Add(parameterName);
            }
        }

        static Dictionary<int, string> ScanIntUsage(VRCAvatarDescriptor vrc, string parameterName)
        {
            var values = new Dictionary<int, string> { { 0, null } };
            foreach (var layer in vrc.baseAnimationLayers)
            {
                if (layer.animatorController is AnimatorController controller)
                {
                    foreach (var animatorLayer in controller.layers)
                    {
                        ScanMachine(animatorLayer.stateMachine, parameterName, values);
                    }
                }
            }
            return values;
        }

        static void ScanMachine(AnimatorStateMachine machine, string parameterName, Dictionary<int, string> values)
        {
            if (machine == null)
            {
                return;
            }
            foreach (var child in machine.states)
            {
                foreach (var transition in child.state.transitions)
                {
                    ScanConditions(transition, parameterName, values);
                }
            }
            foreach (var transition in machine.anyStateTransitions)
            {
                ScanConditions(transition, parameterName, values);
            }
            foreach (var transition in machine.entryTransitions)
            {
                ScanConditions(transition, parameterName, values);
            }
            foreach (var child in machine.stateMachines)
            {
                ScanMachine(child.stateMachine, parameterName, values);
            }
        }

        static void ScanConditions(AnimatorTransitionBase transition, string parameterName, Dictionary<int, string> values)
        {
            foreach (var condition in transition.conditions)
            {
                if (condition.parameter != parameterName)
                {
                    continue;
                }
                int value = Mathf.Abs(condition.threshold - Mathf.Round(condition.threshold)) < 0.001f
                    ? (int)Mathf.Round(condition.threshold)
                    : (int)condition.threshold;

                // Only "Equals" identifies a value with a state; Greater/Less/NotEqual say the
                // value is referenced but not which state it selects.
                string name = null;
                if (condition.mode == AnimatorConditionMode.Equals && transition.destinationState != null)
                {
                    name = UsefulStateName(transition.destinationState.name);
                }
                if (!values.TryGetValue(value, out var existing) || (existing == null && name != null))
                {
                    values[value] = name;
                }
            }
        }

        static string UsefulStateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            string trimmed = name.Trim();
            string lower = trimmed.ToLowerInvariant();
            if (lower == "blend tree" || lower == "new state" || lower.StartsWith("state ") ||
                lower == "on" || lower == "off" || lower == "idle" || lower == "default" ||
                trimmed.All(char.IsDigit))
            {
                return null;
            }
            return trimmed;
        }

        // Menu labels naming the source system read YAPS after conversion.
        // Display only: parameter names stay, since ChilloutVR restores a
        // saved profile by parameter name. DPS is not matched; it also means
        // damage per second.
        static string YapsLabel(BridgeContext ctx, string display)
        {
            if (ctx == null || !ctx.Settings.convertYapsSystems || string.IsNullOrEmpty(display))
            {
                return display;
            }
            return System.Text.RegularExpressions.Regex.Replace(
                display, @"\b(?:SPS|TPS)(?=\d|\b)", "YAPS");
        }

        static string CleanMenuName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "-";
            }
            string clean = System.Text.RegularExpressions.Regex.Replace(name, "<[^>]*>", "");
            clean = clean.Replace("\r", " ").Replace("\n", " ");
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();
            return string.IsNullOrEmpty(clean) ? "-" : clean;
        }

        static string ShortName(string display)
        {
            int slash = display.LastIndexOf('/');
            return slash >= 0 && slash < display.Length - 1 ? display.Substring(slash + 1) : display;
        }

        static string FriendlyMenuName(string fullPath, Dictionary<string, int> leafCounts)
        {
            string leaf = ShortName(fullPath);
            if (leafCounts.TryGetValue(leaf, out var count) && count > 1)
            {
                var segments = fullPath.Split('/');
                if (segments.Length >= 2)
                {
                    return $"{leaf} ({segments[segments.Length - 2]})";
                }
            }
            return leaf;
        }

        static string FriendlyParamName(string name)
        {
            string clean = System.Text.RegularExpressions.Regex.Replace(name, @"^VF\d+_", "");
            return ShortName(clean);
        }

        static string MakeUnique(string name, HashSet<string> usedNames)
        {
            string candidate = name;
            int suffix = 2;
            while (!usedNames.Add(candidate))
            {
                candidate = $"{name} ({suffix++})";
            }
            return candidate;
        }
    }
}
#endif
