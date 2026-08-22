// One socket's marker pair lit at a time, chosen from the menu. A
// disabled light never enters Unity's per-mesh ranking, so every
// lit-capable socket carries a pair and exactly one competes for the
// four vertex slots. Legacy DPS content sees one clean root and front
// wherever the wearer points the lighthouse; contact readers never
// needed the lights at all.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsLighthouse
    {
        public const string Parameter = "YAPS/Lighthouse";
        const string LayerName = "YAPS lighthouse";
        const string MenuLabel = "Marker lights";

        // The lit-capable sockets in cap order, menu entry and selector
        // layer to match. One or none: both are removed, a single pair
        // needs no chooser. Safe to run again after any socket changes.
        public static string Build(CVRAvatar avatar, AnimatorController controller)
        {
            if (avatar == null || controller == null) return null;
            var sockets = avatar.GetComponentsInChildren<YapsSocket>(true)
                .Where(s => s != null && s.emitLights
                    && s.transform.Find(YapsSocketBuilder.LightsName) != null)
                .ToList();
            if (sockets.Count > 1)
            {
                var ordered = YapsSocketBuilder.Lit(sockets[0]);
                sockets = ordered.Where(sockets.Contains).ToList();
            }

            if (sockets.Count < 2)
            {
                bool removed = RemoveEntry(avatar) | RemoveLayer(controller);
                return removed ? "lighthouse removed: one socket needs no chooser" : null;
            }

            // Row 0 is Off: nothing lit until the wearer chooses. A default
            // that lit the first hole left toys working at a socket the
            // wearer had not switched on and failing at the one they had.
            var labels = new List<string> { OffLabel };
            foreach (var s in sockets)
            {
                string label = YapsToggles.LabelFor(s);
                // Two same-kind sockets on one bone read the same; number
                // the later one so the menu rows stay tellable apart.
                for (int n = 2; labels.Contains(label); n++) label = YapsToggles.LabelFor(s) + " " + n;
                labels.Add(label);
            }

            EnsureEntry(avatar, labels);
            if (!controller.parameters.Any(p => p.name == Parameter))
            {
                controller.AddParameter(Parameter, AnimatorControllerParameterType.Int);
            }
            RemoveLayer(controller);
            AddLayer(avatar, controller, sockets);
            return $"lighthouse: {sockets.Count} sockets on one menu, Off until chosen " +
                   "(choosing one lights it and switches it on; the selector syncs, 32 bits)";
        }

        const string OffLabel = "Off";

        static void EnsureEntry(CVRAvatar avatar, List<string> labels)
        {
            if (avatar.avatarSettings == null)
            {
                avatar.avatarSettings = new CVRAdvancedAvatarSettings
                {
                    settings = new List<CVRAdvancedSettingsEntry>(),
                    initialized = true,
                };
            }
            avatar.avatarUsesAdvancedSettings = true;
            var settings = avatar.avatarSettings.settings;
            var options = labels.Select(l => new CVRAdvancedSettingsDropDownEntry { name = l }).ToList();

            var ours = settings.FirstOrDefault(e => e != null && e.machineName == Parameter);
            Undo.RecordObject(avatar, "YAPS lighthouse");
            if (ours == null)
            {
                settings.Add(new CVRAdvancedSettingsEntry
                {
                    name = MenuLabel,
                    machineName = Parameter,
                    type = CVRAdvancedSettingsEntry.SettingsType.Dropdown,
                    dropDownSettings = new CVRAdvancesAvatarSettingGameObjectDropdown
                    {
                        defaultValue = 0,
                        usedType = CVRAdvancesAvatarSettingBase.ParameterType.Int,
                        options = options,
                    },
                });
            }
            else
            {
                ours.name = MenuLabel;
                ours.type = CVRAdvancedSettingsEntry.SettingsType.Dropdown;
                if (ours.dropDownSettings == null)
                {
                    ours.dropDownSettings = new CVRAdvancesAvatarSettingGameObjectDropdown();
                }
                ours.dropDownSettings.usedType = CVRAdvancesAvatarSettingBase.ParameterType.Int;
                ours.dropDownSettings.options = options;
                if (ours.dropDownSettings.defaultValue >= options.Count)
                {
                    ours.dropDownSettings.defaultValue = 0;
                }
            }
            EditorUtility.SetDirty(avatar);
        }

        static bool RemoveEntry(CVRAvatar avatar)
        {
            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            var ours = settings?.FirstOrDefault(e => e != null && e.machineName == Parameter);
            if (ours == null) return false;
            Undo.RecordObject(avatar, "YAPS lighthouse");
            settings.Remove(ours);
            EditorUtility.SetDirty(avatar);
            return true;
        }

        static bool RemoveLayer(AnimatorController controller)
        {
            bool removed = false;
            for (int i = controller.layers.Length - 1; i >= 0; i--)
            {
                if (controller.layers[i].name != LayerName) continue;
                controller.RemoveLayer(i);
                removed = true;
            }
            return removed;
        }

        // State 0 is Off: every pair dark, no socket touched. State i
        // enables socket i's pair, disables every other pair, and switches
        // socket i itself ON — the one thing a DPS toy needs is then one
        // choice. Other sockets' active state is left to their own
        // toggles; only the chosen one is asserted. Any-state transitions
        // on the selector, so the order the wearer clicks in never matters.
        static void AddLayer(CVRAvatar avatar, AnimatorController controller, List<YapsSocket> sockets)
        {
            var pairPaths = sockets
                .Select(s => AnimationUtility.CalculateTransformPath(
                    s.transform.Find(YapsSocketBuilder.LightsName), avatar.transform))
                .ToList();
            var socketPaths = sockets
                .Select(s => AnimationUtility.CalculateTransformPath(s.transform, avatar.transform))
                .ToList();

            var machine = new AnimatorStateMachine
            {
                name = LayerName,
                hideFlags = HideFlags.HideInHierarchy,
            };
            string assetPath = AssetDatabase.GetAssetPath(controller);
            if (!string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.AddObjectToAsset(machine, controller);
            }

            for (int i = 0; i <= sockets.Count; i++)
            {
                int chosen = i - 1;   // -1 is Off
                var clip = new AnimationClip { name = chosen < 0 ? "YAPS lighthouse off" : $"YAPS lighthouse {chosen}" };
                for (int p = 0; p < pairPaths.Count; p++)
                {
                    clip.SetCurve(pairPaths[p], typeof(GameObject), "m_IsActive",
                        AnimationCurve.Constant(0f, 1f / 60f, p == chosen ? 1f : 0f));
                }
                if (chosen >= 0)
                {
                    clip.SetCurve(socketPaths[chosen], typeof(GameObject), "m_IsActive",
                        AnimationCurve.Constant(0f, 1f / 60f, 1f));
                }
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.AddObjectToAsset(clip, controller);
                }
                var state = machine.AddState(chosen < 0 ? "Off" : $"Socket {chosen}");
                state.writeDefaultValues = false;
                state.motion = clip;
                if (i == 0) machine.defaultState = state;

                var to = machine.AddAnyStateTransition(state);
                to.hasExitTime = false;
                to.duration = 0f;
                to.canTransitionToSelf = false;
                to.AddCondition(AnimatorConditionMode.Equals, i, Parameter);
            }

            var layers = controller.layers.ToList();
            layers.Add(new AnimatorControllerLayer
            {
                name = LayerName,
                defaultWeight = 1f,
                stateMachine = machine,
            });
            controller.layers = layers.ToArray();
            EditorUtility.SetDirty(controller);
        }
    }
}
#endif
