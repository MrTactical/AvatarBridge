// One dropdown per plug: which of the sockets it was built to answer it
// will answer right now. SPS calls the same idea Change SPS Tag.
//
// A dropdown, not a row per tag: fifteen toggles and parameters for a
// question with one answer at a time.
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
        const string KeyPrefix = "YAPS/Tags/";

        // Only plugs listing more than one tag get a chooser.
        public static string Build(CVRAvatar avatar, AnimatorController controller)
        {
            if (avatar == null || controller == null) return null;

            var notes = new List<string>();
            var live = new HashSet<string>();
            foreach (var plug in avatar.GetComponentsInChildren<YapsPlug>(true))
            {
                if (plug == null || plug.Target == null) continue;
                var tags = Listed(plug.answers);
                string parameter = Parameter(avatar, plug);
                live.Add(parameter);

                // Listed caps at what the bake holds. Say so: a row missing
                // from the menu reads as a tag that was never typed.
                int over = YapsTags.Dropped(plug.answers);
                if (over > 0)
                {
                    notes.Add(YapsToggles.MenuLabelFor(plug) + ": " + BridgeElements.Count(over, "tag", "tags") + " past the first "
                              + YapsTags.PlugSlots + " have no row, and the bake did not keep them either");
                }
                if (tags.Count < 2)
                {
                    if (Remove(avatar, controller, parameter))
                    {
                        notes.Add(YapsToggles.MenuLabelFor(plug) + ": tag chooser removed, fewer than two tags");
                    }
                    continue;
                }

                var labels = new List<string> { "As built" };
                labels.AddRange(tags.Select(Pretty));
                labels.Add("Anything");

                EnsureEntry(avatar, parameter, YapsToggles.MenuLabelFor(plug) + " answers", labels);
                RemoveLayer(controller, parameter);
                AddLayer(avatar, controller, plug, parameter, tags);
                notes.Add(YapsToggles.MenuLabelFor(plug) + ": tag chooser added (" + parameter + "), "
                          + tags.Count + " tags, \"As built\" by default");
            }

            // A chooser keyed on no plug (deleted, or its mesh moved); the loop
            // above never reaches it.
            var keys = controller.layers.Where(l => l.name.StartsWith(LayerPrefix))
                .Select(l => l.name.Substring(LayerPrefix.Length))
                .Concat(controller.parameters.Select(p => p.name));
            if (avatar.avatarSettings != null && avatar.avatarSettings.settings != null)
                keys = keys.Concat(avatar.avatarSettings.settings.Where(e => e != null).Select(e => e.machineName));
            foreach (string key in keys.Where(k => k != null && k.StartsWith(KeyPrefix) && !live.Contains(k))
                         .Distinct().ToList())
            {
                if (Remove(avatar, controller, key)) notes.Add("tag chooser " + key + " removed, no plug uses it");
            }
            return notes.Count > 0 ? string.Join("; ", notes) : null;
        }

        // Entry, layer and Int. The Int goes only once nothing reads it, and
        // left behind it still synced.
        static bool Remove(CVRAvatar avatar, AnimatorController controller, string parameter)
        {
            bool removed = RemoveEntry(avatar, parameter) | RemoveLayer(controller, parameter);
            int index = System.Array.FindIndex(controller.parameters, p => p.name == parameter);
            if (index >= 0 && !YapsRemover.ParameterUsed(controller, parameter))
            {
                controller.RemoveParameter(index);
                EditorUtility.SetDirty(controller);
                removed = true;
            }
            return removed;
        }

        // Normalised and capped by the bake's own call: two copies disagreed.
        static List<string> Listed(IList<string> tags)
        {
            var found = YapsTags.Listed(tags);
            if (found.Count > YapsTags.PlugSlots) found.RemoveRange(YapsTags.PlugSlots, found.Count - YapsTags.PlugSlots);
            return found;
        }

        // "handleft" reads as "Hand left"; an author's own word keeps its shape.
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

        // Keyed on the plug's path, not its label, which follows the bone.
        // The second plug on a shared mesh keys on "#1" too, so each gets its own chooser.
        internal static string Parameter(CVRAvatar avatar, YapsPlug plug)
        {
            string path = AnimationUtility.CalculateTransformPath(plug.Target.transform, avatar.transform);
            if (LaneOf(plug) > 0) path += "#1";
            return KeyPrefix + (path.GetHashCode() & 0x7FFFFF).ToString("X6");
        }

        static int LaneOf(YapsPlug plug) => YapsToggles.LaneOn(plug, plug.Target);

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

        // State 0 restores the bake's list, state i narrows to one tag, the
        // last clears it ("Anything"). Refusing always beats answering, so it
        // never reaches a socket the author ruled out.
        static void AddLayer(CVRAvatar avatar, AnimatorController controller, YapsPlug plug,
            string parameter, List<string> tags)
        {
            if (!controller.parameters.Any(p => p.name == parameter))
            {
                controller.AddParameter(parameter, AnimatorControllerParameterType.Int);
            }

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

            var values = new List<Vector4> { Vector(tags) };
            values.AddRange(tags.Select(t => Vector(new List<string> { t })));
            values.Add(Vector4.zero);

            // Every mesh of the plug, or the others keep answering as built.
            var targets = YapsToggles.Targets(plug, avatar.transform);
            for (int i = 0; i < values.Count; i++)
            {
                // Plugs on one mesh share a menu label, so the second's clips are named apart.
                bool second = LaneOf(plug) > 0;
                var clip = YapsToggles.Clip(targets, second ? "_YAPS_TagInclude2" : "_YAPS_TagInclude", values[i],
                    dir + "/" + Sanitise(YapsToggles.MenuLabelFor(plug)) + (second ? " second" : "") + " answers " + i + ".anim");
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

        // Matches the bake; not shared, since the builder's takes the raw list.
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
