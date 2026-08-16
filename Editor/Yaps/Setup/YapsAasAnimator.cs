// A menu entry's layer and parameter, written into the animators the
// avatar wears through the CCK's own SetupAnimator. No copied
// controller, no replaced overrides.
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
        // The entry's layer and parameter into every controller the avatar
        // plays that lacks the parameter. Returns what happened, or null
        // when there was nothing to do.
        public static string Wire(CVRAvatar avatar, CVRAdvancedSettingsEntry entry)
        {
            if (avatar == null || entry == null || entry.setting == null || string.IsNullOrEmpty(entry.machineName)) return null;
            var controllers = ControllersOf(avatar);
            if (controllers.Count == 0)
                return $"{entry.name}: no animator controller on the avatar to put its layer in; set one on the Animator or the CVRAvatar's base controller";

            string folder = YapsNativeBuilder.OutputRoot + "/" + Sanitise(avatar.name);
            YapsNativeBuilder.EnsureFolderPublic(folder);
            string fileName = Regex.Replace(entry.machineName, "[^a-zA-Z0-9_]+", "");
            var into = new List<string>();
            foreach (var controller in controllers)
            {
                if (controller.parameters.Any(p => p.name == entry.machineName)) continue;
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(controller))) continue;
                Undo.RegisterCompleteObjectUndo(controller, "YAPS menu entry");
                var c = controller;
                entry.setting.SetupAnimator(ref c, entry.machineName, folder, fileName);
                EditorUtility.SetDirty(controller);
                into.Add(controller.name);
            }
            if (into.Count == 0) return null;
            AssetDatabase.SaveAssets();
            return $"{entry.name}: layer and parameter \"{entry.machineName}\" written into {string.Join(" and ", into.Select(n => "\"" + n + "\""))}, " +
                   "in place, as the CCK's Create Animator would";
        }

        // The layer and parameter of an entry the toolkit removed, out of
        // every controller the avatar plays.
        public static string Unwire(CVRAvatar avatar, string machineName)
        {
            if (avatar == null || string.IsNullOrEmpty(machineName)) return null;
            var from = new List<string>();
            foreach (var controller in ControllersOf(avatar))
            {
                var layers = controller.layers;
                int index = System.Array.FindIndex(layers, l => l.name == machineName);
                var p = controller.parameters.FirstOrDefault(x => x.name == machineName);
                if (index < 0 && p == null) continue;
                Undo.RegisterCompleteObjectUndo(controller, "YAPS menu entry");
                if (index >= 0) controller.RemoveLayer(index);
                if (p != null && !controller.layers.Any(l => l.name == machineName)) controller.RemoveParameter(p);
                EditorUtility.SetDirty(controller);
                from.Add(controller.name);
            }
            if (from.Count == 0) return null;
            AssetDatabase.SaveAssets();
            return $"layer and parameter \"{machineName}\" taken out of {string.Join(" and ", from.Select(n => "\"" + n + "\""))}";
        }

        // Every animator controller the avatar plays, once each: the
        // Advanced Settings base controller and the one on the Animator,
        // an override controller resolved to what it overrides.
        public static List<AnimatorController> ControllersOf(CVRAvatar avatar)
        {
            var list = new List<AnimatorController>();
            void Add(RuntimeAnimatorController c)
            {
                if (c is AnimatorOverrideController o) c = o.runtimeAnimatorController;
                if (c is AnimatorController a && !list.Contains(a)) list.Add(a);
            }
            if (avatar == null) return list;
            if (avatar.avatarSettings != null) Add(avatar.avatarSettings.baseController);
            var animator = avatar.GetComponent<Animator>();
            if (animator != null) Add(animator.runtimeAnimatorController);
            return list;
        }

        static string Sanitise(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
#endif
