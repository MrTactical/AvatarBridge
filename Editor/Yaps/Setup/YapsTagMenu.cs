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

        // The bits actually set, low to high, so the menu order is stable.
        static List<YapsTags> Listed(YapsTags set)
        {
            var found = new List<YapsTags>();
            for (int b = 0; b < 15; b++)
            {
                var one = (YapsTags) (1 << b);
                if ((set & one) != 0) found.Add(one);
            }
            return found;
        }

        // HandLeft reads as "Hand left" in a menu. The enum is the protocol,
        // the label is for a person.
        static string Pretty(YapsTags one)
        {
            string name = one.ToString();
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i])) text.Append(' ').Append(char.ToLowerInvariant(name[i]));
                else text.Append(name[i]);
            }
            return text.ToString();
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
            string parameter, List<YapsTags> tags)
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

            var values = new List<int> { (int) plug.answers };
            values.AddRange(tags.Select(t => (int) t));
            values.Add(0);

            // EVERY slot the plug wears, not slot 0. "material._X" binds to the
            // first material alone, so a plug modelled with its tip on a second
            // material would answer one set of tags across half its mesh and
            // another across the rest, and the shaft would tear at the seam.
            var slots = Slots(plug);

            for (int i = 0; i < values.Count; i++)
            {
                var clip = Clip(plugPath, plug.Target.GetType(), slots, values[i],
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

        // Which material slots on the plug's renderer carry the property. A
        // slot without it would animate a binding nothing reads, which Unity
        // reports as a missing curve on every avatar that has one.
        static List<int> Slots(YapsPlug plug)
        {
            var found = new List<int>();
            var renderer = plug.Target;
            var materials = renderer != null ? renderer.sharedMaterials : null;
            if (materials == null) return found;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].HasProperty("_YAPS_TagInclude")) found.Add(i);
            }
            return found;
        }

        static AnimationClip Clip(string path, System.Type type, List<int> slots, float value, string assetPath)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, assetPath);
            }
            clip.ClearCurves();
            foreach (int slot in slots)
            {
                string property = slot == 0
                    ? "material._YAPS_TagInclude"
                    : "material[" + slot + "]._YAPS_TagInclude";
                clip.SetCurve(path, type, property, AnimationCurve.Constant(0f, 1f / 60f, value));
            }
            EditorUtility.SetDirty(clip);
            return clip;
        }

        static string Sanitise(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }
    }
}
#endif
