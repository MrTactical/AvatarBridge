#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
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
    /// <summary>
    /// Converts VRChat expression parameters + expression menus into ChilloutVR
    /// Advanced Avatar Settings entries:
    ///
    ///   Bool  + menu toggle/button  -> Toggle
    ///   Int   + menu toggles        -> Dropdown (one option per used value) or Toggle
    ///   Float + radial/puppet menu  -> Slider
    ///
    /// The generated entries deliberately have no GameObject targets: the converted FX
    /// layers already react to the parameters, the entries only exist so CVR shows menu
    /// controls and syncs the values.
    /// </summary>
    public static class ParameterMenuConverter
    {
        const string Category = "Parameters & menu";

        class MenuUse
        {
            public string DisplayName;
            public float Value;
            public VRCExpressionsMenu.Control.ControlType Type;
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
            WalkMenu(vrc.expressionsMenu, "", uses, visited, ctx);

            var entries = new List<CVRAdvancedSettingsEntry>();
            var usedNames = new HashSet<string>();

            // Menu entries show the leaf name ("Cloak"), qualified by their parent menu
            // only when several controls share the same leaf ("Hoodie (Tops)").
            var leafCounts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var pair in uses)
            {
                string leaf = ShortName(pair.Value[0].DisplayName);
                leafCounts[leaf] = leafCounts.TryGetValue(leaf, out var n) ? n + 1 : 1;
            }

            if (vrcParams != null && vrcParams.parameters != null)
            {
                foreach (var p in vrcParams.parameters)
                {
                    if (string.IsNullOrEmpty(p.name))
                    {
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

            ctx.CvrAvatar.avatarSettings.settings = entries;
            UnityEditor.EditorUtility.SetDirty(ctx.CvrAvatar);
            ctx.Report.Converted(Category, $"{entries.Count} Advanced Avatar Settings entries created");
        }

        static CVRAdvancedSettingsEntry BuildEntry(BridgeContext ctx, VRCExpressionParameters.Parameter p,
            List<MenuUse> paramUses, HashSet<string> usedNames, Dictionary<string, int> leafCounts)
        {
            bool hasMenu = paramUses != null && paramUses.Count > 0;
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

            string display = hasMenu
                ? FriendlyMenuName(paramUses[0].DisplayName, leafCounts)
                : FriendlyParamName(p.name);
            display = MakeUnique(display, usedNames);

            // Menu buttons become impulse parameters (auto-reset shortly after being set).
            if (hasMenu && paramUses.All(u => u.Type == VRCExpressionsMenu.Control.ControlType.Button))
            {
                ctx.ImpulseParameters.Add(p.name);
            }

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
                foreach (int v in ScanIntValues(ctx.SourceDescriptor, p.name))
                {
                    valueNames[v] = p.name + " = " + v;
                }
            }
            int maxValue = Mathf.Max(valueNames.Count > 0 ? valueNames.Keys.Max() : 0, (int)p.defaultValue);
            maxValue = Mathf.Min(maxValue, 255);

            var options = new List<CVRAdvancedSettingsDropDownEntry>();
            for (int i = 0; i <= maxValue; i++)
            {
                options.Add(new CVRAdvancedSettingsDropDownEntry
                {
                    name = valueNames.TryGetValue(i, out var label) ? label : "---"
                });
            }

            ctx.Report.Converted(Category, p.name, $"Dropdown \"{display}\" with {options.Count} options");
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
            Dictionary<string, List<MenuUse>> uses, HashSet<VRCExpressionsMenu> visited, BridgeContext ctx)
        {
            if (menu == null || visited.Contains(menu))
            {
                return;
            }
            visited.Add(menu);

            foreach (var control in menu.controls)
            {
                string display = prefix + CleanMenuName(control.name);
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
                        break;

                    case VRCExpressionsMenu.Control.ControlType.FourAxisPuppet:
                        RecordSubParameter(uses, ctx, control, 0, display + " (Up)", control.type);
                        RecordSubParameter(uses, ctx, control, 1, display + " (Right)", control.type);
                        RecordSubParameter(uses, ctx, control, 2, display + " (Down)", control.type);
                        RecordSubParameter(uses, ctx, control, 3, display + " (Left)", control.type);
                        break;

                    case VRCExpressionsMenu.Control.ControlType.SubMenu:
                        WalkMenu(control.subMenu, display + "/", uses, visited, ctx);
                        break;
                }
            }
        }

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

        /// <summary>
        /// For int parameters without menu controls: find every value the animators compare
        /// against, so the generated dropdown covers all meaningful states.
        /// </summary>
        static IEnumerable<int> ScanIntValues(VRCAvatarDescriptor vrc, string parameterName)
        {
            var values = new HashSet<int> { 0 };
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

        static void ScanMachine(AnimatorStateMachine machine, string parameterName, HashSet<int> values)
        {
            if (machine == null)
            {
                return;
            }
            foreach (var child in machine.states)
            {
                foreach (var transition in child.state.transitions)
                {
                    ScanConditions(transition.conditions, parameterName, values);
                }
            }
            foreach (var transition in machine.anyStateTransitions)
            {
                ScanConditions(transition.conditions, parameterName, values);
            }
            foreach (var transition in machine.entryTransitions)
            {
                ScanConditions(transition.conditions, parameterName, values);
            }
            foreach (var child in machine.stateMachines)
            {
                ScanMachine(child.stateMachine, parameterName, values);
            }
        }

        static void ScanConditions(AnimatorCondition[] conditions, string parameterName, HashSet<int> values)
        {
            foreach (var condition in conditions)
            {
                if (condition.parameter == parameterName)
                {
                    values.Add(Mathf.Abs(condition.threshold - Mathf.Round(condition.threshold)) < 0.001f
                        ? (int)Mathf.Round(condition.threshold)
                        : (int)condition.threshold);
                }
            }
        }

        /// <summary>
        /// Menu names frequently carry TextMeshPro markup (GoGo Loco headers etc.);
        /// CVR menus render them literally, so strip tags and newlines.
        /// </summary>
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

        /// <summary>Menuless parameters: drop VRCFury's "VF12_" id prefix and any path.</summary>
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
