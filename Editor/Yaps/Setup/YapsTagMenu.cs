// One dropdown per plug: which of the sockets it was built to answer it
// will answer right now. SPS calls the same idea Change SPS Tag.
//
// A dropdown and a layer rather than a row per tag. Fifteen toggles would
// be fifteen menu rows and fifteen parameters for a question with one
// answer at a time, which is the objection that killed the per-socket
// allow list.
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
    public static class YapsTagMenu
    {
        const string LayerPrefix = "YAPS tags ";

        // Only a plug that lists more than one tag gets a chooser: with one
        // tag there is nothing to choose between, and with none the plug
        // already answers everything.
        public static string Build(CVRAvatar avatar, AnimatorController controller)
        {
            if (avatar == null || controller == null) return null;

            var notes = new List<string>();
            foreach (var plug in avatar.GetComponentsInChildren<YapsPlug>(true))
            {
                if (plug == null || plug.Target == null) continue;
                var tags = Listed(plug.answers);
                string parameter = Parameter(avatar, plug);
                if (tags.Count < 2)
                {
                    if (RemoveEntry(avatar, parameter) | RemoveLayer(controller, parameter))
                    {
                        notes.Add(YapsToggles.LabelFor(plug) + ": tag chooser removed, fewer than two tags");
                    }
                    continue;
                }

                var labels = new List<string> { "As built" };
                labels.AddRange(tags.Select(Pretty));
                labels.Add("Anything");

                EnsureEntry(avatar, parameter, YapsToggles.LabelFor(plug) + " answers", labels);
                RemoveLayer(controller, parameter);
                AddLayer(avatar, controller, plug, parameter, tags);
                notes.Add(YapsToggles.LabelFor(plug) + ": tag chooser added (" + parameter + "), "
                          + tags.Count + " tags, \"As built\" by default");
            }
            return notes.Count > 0 ? string.Join("; ", notes) : null;
        }

        // The tags the author listed, in the order they wrote them, minus
        // blanks and repeats. The order is the menu's order, so it has to be
        // theirs rather than sorted: a list they can read top to bottom in
        // the inspector is the same list they see in game.
        static List<string> Listed(IList<string> tags)
        {
            var found = new List<string>();
            if (tags == null) return found;
            foreach (string tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                string clean = tag.Trim();
                if (found.Any(f => string.Equals(f, clean, System.StringComparison.OrdinalIgnoreCase))) continue;
                found.Add(clean);
                if (found.Count >= YapsTags.PlugSlots) break;
            }
            return found;
        }

        // "handleft" reads as "Hand left" in a menu, and an author's own
        // word keeps whatever shape they gave it. The string is the protocol,
        // the label is for a person.
        static string Pretty(string tag)
        {
            foreach (string known in YapsTags.Suggested)
            {
                if (!string.Equals(known, tag, System.StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string part in new[] { "front", "back", "left", "right" })
                {
                    if (known.Length > part.Length && known.EndsWith(part, System.StringComparison.Ordinal))
                    {
                        string head = known.Substring(0, known.Length - part.Length);
                        return char.ToUpperInvariant(head[0]) + head.Substring(1) + " " + part;
                    }
                }
                return char.ToUpperInvariant(known[0]) + known.Substring(1);
            }
            return tag;
        }

        // Keyed on the plug's path, not its label: a label follows the bone
        // and can change, and a renamed entry would leave the old one behind
        // driving a layer nothing removed.
        static string Parameter(CVRAvatar avatar, YapsPlug plug)
        {
            string path = AnimationUtility.CalculateTransformPath(plug.Target.transform, avatar.transform);
            return "YAPS/Tags/" + (path.GetHashCode() & 0x7FFFFF).ToString("X6");
        }

        static void EnsureEntry(CVRAvatar avatar, string parameter, string label, List<string> labels)
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

            var ours = settings.FirstOrDefault(e => e != null && e.machineName == parameter);
            Undo.RecordObject(avatar, "YAPS tag chooser");
            if (ours == null)
            {
                settings.Add(new CVRAdvancedSettingsEntry
                {
                    name = label,
                    machineName = parameter,
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
                ours.name = label;
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

        static bool RemoveEntry(CVRAvatar avatar, string parameter)
        {
            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            var ours = settings == null ? null : settings.FirstOrDefault(e => e != null && e.machineName == parameter);
            if (ours == null) return false;
            Undo.RecordObject(avatar, "YAPS tag chooser");
            settings.Remove(ours);
            EditorUtility.SetDirty(avatar);
            return true;
        }

        static bool RemoveLayer(AnimatorController controller, string parameter)
        {
            bool removed = false;
            for (int i = controller.layers.Length - 1; i >= 0; i--)
            {
                if (controller.layers[i].name != LayerPrefix + parameter) continue;
                controller.RemoveLayer(i);
                removed = true;
            }
            return removed;
        }

        // State 0 restores what the bake wrote, so a wearer who never opens
        // the menu gets the author's answer. State i narrows to one tag.
        // The last state clears the include set, which answers anything the
        // plug's refuse list does not deny: refusing always beats answering,
        // so "Anything" can never reach a socket the author ruled out.
        static void AddLayer(CVRAvatar avatar, AnimatorController controller, YapsPlug plug,
            string parameter, List<string> tags)
        {
            if (!controller.parameters.Any(p => p.name == parameter))
            {
                controller.AddParameter(parameter, AnimatorControllerParameterType.Int);
            }

            string plugPath = AnimationUtility.CalculateTransformPath(plug.Target.transform, avatar.transform);
            string dir = YapsNativeBuilder.OutputRoot + "/" + Sanitise(avatar.name);
            YapsNativeBuilder.EnsureFolderPublic(dir);

            var machine = new AnimatorStateMachine
            {
                name = LayerPrefix + parameter,
                hideFlags = HideFlags.HideInHierarchy,
            };
            string assetPath = AssetDatabase.GetAssetPath(controller);
            if (!string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.AddObjectToAsset(machine, controller);
            }

            // State 0 is the whole list the author wrote, state i narrows to
            // one tag in the first slot, and the last clears every slot.
            var values = new List<Vector4> { Vector(tags) };
            values.AddRange(tags.Select(t => Vector(new List<string> { t })));
            values.Add(Vector4.zero);

            for (int i = 0; i < values.Count; i++)
            {
                var clip = YapsToggles.Clip(plugPath, plug.Target, "_YAPS_TagInclude", values[i],
                    dir + "/" + Sanitise(YapsToggles.LabelFor(plug)) + " answers " + i + ".anim");
                var state = machine.AddState("Answers " + i);
                state.writeDefaultValues = false;
                state.motion = clip;
                if (i == 0) machine.defaultState = state;

                var to = machine.AddAnyStateTransition(state);
                to.hasExitTime = false;
                to.duration = 0f;
                to.canTransitionToSelf = false;
                to.AddCondition(AnimatorConditionMode.Equals, i, parameter);
            }

            var layers = controller.layers.ToList();
            layers.Add(new AnimatorControllerLayer
            {
                name = LayerPrefix + parameter,
                defaultWeight = 1f,
                stateMachine = machine,
            });
            controller.layers = layers.ToArray();
            EditorUtility.SetDirty(controller);
        }

        // Patterns in a vector, matching what the bake wrote. Kept here rather
        // than shared with the builder's copy because that one takes the
        // author's raw list and this one takes a list already cleaned.
        static Vector4 Vector(List<string> tags)
        {
            var v = Vector4.zero;
            for (int i = 0; i < tags.Count && i < YapsTags.PlugSlots; i++) v[i] = YapsTags.Pattern(tags[i]);
            return v;
        }

        static string Sanitise(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }
    }
}
#endif
