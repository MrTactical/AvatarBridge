// The CCK builds the Advanced Settings animator when the user presses
// Create Animator on the CVRAvatar: the base controller copied, a layer
// and a parameter per menu entry through each entry's own SetupAnimator,
// an override controller on top. A toggle the toolkit adds is not in the
// menu until that runs, and a line saying "press Create Animator" was
// easy to miss. So the toolkit runs the same steps itself, by the CCK's
// public SetupAnimator, whenever it changed the entries. Two habits it
// respects: a base controller that IS the generated one, common, is
// worked in place, as the CCK does; and a generated controller edited by
// hand while the base is a separate file is left alone, with a note to
// press the button, since regenerating would discard those edits.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsAasAnimator
    {
        public const string Folder = "Assets/AdvancedSettings.Generated";

        // Makes or refreshes the avatar's Advanced Settings animator, and
        // drops the layers and parameters of entries the toolkit removed.
        // Returns what happened; false when it would not touch it.
        public static bool Regenerate(CVRAvatar avatar, IEnumerable<string> droppedMachineNames, out string note)
        {
            note = null;
            if (avatar == null || avatar.avatarSettings == null) return false;
            var settings = avatar.avatarSettings;
            var baseController = settings.baseController as AnimatorController
                ?? (settings.baseController as AnimatorOverrideController)?.runtimeAnimatorController as AnimatorController;
            if (baseController == null)
            {
                note = "The avatar has no base controller in its Advanced Settings, so the menu animator could not be built: " +
                       "set one on the CVRAvatar and press Create Animator.";
                return false;
            }
            string basePath = AssetDatabase.GetAssetPath(baseController);
            if (string.IsNullOrEmpty(basePath))
            {
                note = "The base controller is not an asset on disk, so the menu animator could not be built.";
                return false;
            }

            string folder = Folder + "/" + avatar.name + "_AAS";
            EnsureFolder(folder);
            string path = folder + "/" + avatar.name + "_aas.controller";
            bool inPlace = basePath == path;

            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!inPlace && existing != null && HandEdited(existing, baseController, settings))
            {
                note = $"The menu animator \"{existing.name}\" has layers that are not in the base controller and not " +
                       "from a menu entry, so it looks edited by hand; the toolkit left it and did not add its entry's " +
                       "layer. Press Create Animator on the CVRAvatar yourself, or move those layers to the base controller.";
                return false;
            }

            AnimatorController animator;
            if (inPlace)
            {
                animator = baseController;
            }
            else
            {
                // A file copy over the old one keeps its meta, so whatever
                // referenced the generated controller still does.
                File.Copy(basePath, path, true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                animator = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            }
            if (animator == null)
            {
                note = "The menu animator could not be written.";
                return false;
            }

            // What the base already drives itself is not generated, as the CCK.
            var baseParams = new HashSet<string>();
            foreach (var p in baseController.parameters)
            {
                if (p.name.Length == 0 || p.name[0] == '#' || CVRCommon.CoreParameters.Contains(p.name)) continue;
                if (p.type == AnimatorControllerParameterType.Float || p.type == AnimatorControllerParameterType.Int
                    || p.type == AnimatorControllerParameterType.Bool)
                    baseParams.Add(p.name);
            }

            Undo.RegisterCompleteObjectUndo(animator, "YAPS menu animator");
            int added = 0;
            foreach (var entry in settings.settings)
            {
                if (entry == null || entry.setting == null) continue;
                bool driven;
                switch (entry.type)
                {
                    case CVRAdvancedSettingsEntry.SettingsType.Color:
                        driven = baseParams.Contains(entry.machineName + "-r") || baseParams.Contains(entry.machineName + "-g")
                                 || baseParams.Contains(entry.machineName + "-b");
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.Joystick2D:
                    case CVRAdvancedSettingsEntry.SettingsType.InputVector2:
                        driven = baseParams.Contains(entry.machineName + "-x") || baseParams.Contains(entry.machineName + "-y");
                        break;
                    case CVRAdvancedSettingsEntry.SettingsType.Joystick3D:
                    case CVRAdvancedSettingsEntry.SettingsType.InputVector3:
                        driven = baseParams.Contains(entry.machineName + "-x") || baseParams.Contains(entry.machineName + "-y")
                                 || baseParams.Contains(entry.machineName + "-z");
                        break;
                    default:
                        driven = baseParams.Contains(entry.machineName);
                        break;
                }
                if (driven) continue;
                string fileName = Regex.Replace(entry.machineName, "[^a-zA-Z0-9_]+", "");
                entry.setting.SetupAnimator(ref animator, entry.machineName, folder, fileName);
                added++;
            }

            // Entries the toolkit removed: their layer and parameter go,
            // which the CCK's own button never does for a base worked in place.
            int dropped = 0;
            foreach (string machine in droppedMachineNames ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(machine)) continue;
                var layers = animator.layers;
                int index = System.Array.FindIndex(layers, l => l.name == machine);
                if (index >= 0) { animator.RemoveLayer(index); dropped++; }
                var p = animator.parameters.FirstOrDefault(x => x.name == machine);
                if (p != null && !animator.layers.Any(l => l.name == machine)) animator.RemoveParameter(p);
            }

            Undo.RecordObject(avatar, "YAPS menu animator");
            settings.animator = animator;

            // The override controller on top, and on the avatar.
            string overridePath = folder + "/" + avatar.name + "_aas_overrides.overrideController";
            AnimatorOverrideController overrides;
            if (settings.baseOverrideController != null)
            {
                string baseOverridePath = AssetDatabase.GetAssetPath(settings.baseOverrideController);
                if (!string.IsNullOrEmpty(baseOverridePath) && baseOverridePath != overridePath)
                {
                    File.Copy(baseOverridePath, overridePath, true);
                    AssetDatabase.ImportAsset(overridePath, ImportAssetOptions.ForceUpdate);
                }
                overrides = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath);
                if (overrides == null) overrides = settings.baseOverrideController as AnimatorOverrideController;
            }
            else
            {
                overrides = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath);
                if (overrides == null)
                {
                    overrides = new AnimatorOverrideController(animator);
                    AssetDatabase.CreateAsset(overrides, overridePath);
                }
            }
            if (overrides != null)
            {
                overrides.runtimeAnimatorController = animator;
                EditorUtility.SetDirty(overrides);
                settings.overrides = overrides;
                // Attach it, unless the avatar's slot holds something else of the user's.
                if (avatar.overrides == null || AssetDatabase.GetAssetPath(avatar.overrides) == overridePath)
                    avatar.overrides = overrides;
                var component = avatar.GetComponent<Animator>();
                if (component != null && component.runtimeAnimatorController == null)
                    component.runtimeAnimatorController = overrides;
            }

            EditorUtility.SetDirty(animator);
            EditorUtility.SetDirty(avatar);
            AssetDatabase.SaveAssets();

            note = $"Menu animator {(inPlace ? "updated in place" : "rebuilt from the base controller")} " +
                   $"(\"{animator.name}\", what the CCK's Create Animator does): " +
                   (added > 0 ? $"{added} entry layer(s) added" : "no new entry layers") +
                   (dropped > 0 ? $", {dropped} removed" : "") + ".";
            if (avatar.overrides != overrides && overrides != null)
                note += " The avatar's Overrides slot holds something else and was left alone.";
            return true;
        }

        // Layers in the generated controller that neither the base nor any
        // menu entry accounts for: someone edited it by hand.
        static bool HandEdited(AnimatorController generated, AnimatorController baseController, CVRAdvancedAvatarSettings settings)
        {
            var known = new HashSet<string>(baseController.layers.Select(l => l.name));
            foreach (var e in settings.settings)
            {
                if (e == null) continue;
                known.Add(e.machineName);
                // Colour, joystick and vector entries make one layer per axis.
                foreach (string suffix in new[] { "-r", "-g", "-b", "-x", "-y", "-z" }) known.Add(e.machineName + suffix);
            }
            foreach (var layer in generated.layers)
            {
                if (known.Contains(layer.name)) continue;
                if (layer.name.StartsWith("YAPS ") && layer.name.EndsWith(" reactions")) continue;
                return true;
            }
            return false;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
