// A socket nobody can switch off holds a light slot forever, and a plug
// with no switch cannot be put away. When nothing on the avatar already
// toggles one, Build gives it an Advanced Settings entry the CCK turns
// into a menu toggle and an animator layer of its own; the user
// regenerates the animator on the CVRAvatar as they would for any entry.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsToggles
    {
        // What already switches this object, by name; null when nothing does.
        // An Advanced Settings entry aiming at it or an ancestor, or a clip in
        // the avatar's animator that drives m_IsActive on it or an ancestor.
        public static string ToggledBy(GameObject target, CVRAvatar avatar)
        {
            if (target == null) return null;
            var chain = new List<Transform>();
            for (var at = target.transform; at != null; at = at.parent)
            {
                chain.Add(at);
                if (avatar != null && at == avatar.transform) break;
            }
            var chainObjects = new HashSet<GameObject>(chain.Select(t => t.gameObject));

            if (avatar != null && avatar.avatarSettings != null && avatar.avatarSettings.settings != null)
            {
                foreach (var entry in avatar.avatarSettings.settings)
                {
                    if (entry == null) continue;
                    if (entry.type == CVRAdvancedSettingsEntry.SettingsType.Toggle && entry.toggleSettings != null)
                    {
                        if (entry.toggleSettings.gameObjectTargets.Any(t => t != null && t.gameObject != null && chainObjects.Contains(t.gameObject)))
                            return $"the setting \"{entry.name}\"";
                    }
                    if (entry.type == CVRAdvancedSettingsEntry.SettingsType.Dropdown && entry.dropDownSettings != null)
                    {
                        foreach (var option in entry.dropDownSettings.options)
                        {
                            if (option != null && option.gameObjectTargets.Any(t => t != null && t.gameObject != null && chainObjects.Contains(t.gameObject)))
                                return $"the setting \"{entry.name}\"";
                        }
                    }
                }
            }

            var animator = avatar != null ? avatar.GetComponent<Animator>() : target.GetComponentInParent<Animator>();
            var root = animator != null ? animator.transform : (avatar != null ? avatar.transform : target.transform.root);
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller != null)
            {
                var paths = new HashSet<string>(chain.Select(t => AnimationUtility.CalculateTransformPath(t, root)));
                foreach (var clip in YapsCurveMirror.ClipsOf(controller))
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type == typeof(GameObject) && binding.propertyName == "m_IsActive"
                            && paths.Contains(binding.path))
                            return $"the animation \"{clip.name}\"";
                    }
                }
            }
            return null;
        }

        // A menu toggle for an object: on and off by activity, default as
        // it stands now. Returns what happened, for the window.
        public static string EnsureObjectToggle(GameObject target, CVRAvatar avatar, string label)
        {
            if (target == null || avatar == null) return null;
            string already = ToggledBy(target, avatar);
            if (already != null) return $"{label}: already switched by {already}";
            if (avatar.avatarSettings == null)
            {
                avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
            }
            avatar.avatarUsesAdvancedSettings = true;
            var settings = avatar.avatarSettings.settings;
            string machine = MachineName(settings, label);
            Undo.RecordObject(avatar, "YAPS toggle");
            settings.Add(new CVRAdvancedSettingsEntry
            {
                name = label,
                machineName = machine,
                type = CVRAdvancedSettingsEntry.SettingsType.Toggle,
                toggleSettings = new CVRAdvancesAvatarSettingGameObjectToggle
                {
                    defaultValue = target.activeSelf,
                    usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool,
                    gameObjectTargets = new List<CVRAdvancedSettingsTargetEntryGameObject>
                    {
                        new CVRAdvancedSettingsTargetEntryGameObject { gameObject = target, onState = true,
                            treePath = AnimationUtility.CalculateTransformPath(target.transform, avatar.transform) },
                    },
                },
            });
            EditorUtility.SetDirty(avatar);
            return $"{label}: menu toggle \"{label}\" added ({machine}); press Create Animator on the CVRAvatar's Advanced Settings to build its layer";
        }

        // A menu toggle for a plug's deform: two clips writing _YAPS_Enabled
        // on its material, on and off, as an Advanced Settings toggle with
        // its own animation.
        public static string EnsurePlugToggle(YapsPlug plug, CVRAvatar avatar, Material material, string label)
        {
            if (plug == null || avatar == null || material == null || plug.Target == null) return null;
            if (avatar.avatarSettings != null && avatar.avatarSettings.settings != null
                && avatar.avatarSettings.settings.Any(e => e != null && e.name == label))
                return $"{label}: menu toggle already there";
            var controller = avatar.GetComponent<Animator>() != null ? avatar.GetComponent<Animator>().runtimeAnimatorController : null;
            if (controller != null)
            {
                string rendererPath = AnimationUtility.CalculateTransformPath(plug.Target.transform, avatar.transform);
                foreach (var clip in YapsCurveMirror.ClipsOf(controller))
                {
                    if (AnimationUtility.GetCurveBindings(clip).Any(b => b.path == rendererPath && b.propertyName == "material._YAPS_Enabled"))
                        return $"{label}: already switched by the animation \"{clip.name}\"";
                }
            }

            string dir = YapsNativeBuilder.OutputRoot + "/" + Sanitise(avatar.name);
            YapsNativeBuilder.EnsureFolderPublic(dir);
            string path = AnimationUtility.CalculateTransformPath(plug.Target.transform, avatar.transform);
            var on = Clip(path, plug.Target.GetType(), 1f, dir + "/" + Sanitise(label) + " on.anim");
            var off = Clip(path, plug.Target.GetType(), 0f, dir + "/" + Sanitise(label) + " off.anim");

            if (avatar.avatarSettings == null)
            {
                avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
            }
            avatar.avatarUsesAdvancedSettings = true;
            var settings = avatar.avatarSettings.settings;
            string machine = MachineName(settings, label);
            Undo.RecordObject(avatar, "YAPS toggle");
            settings.Add(new CVRAdvancedSettingsEntry
            {
                name = label,
                machineName = machine,
                type = CVRAdvancedSettingsEntry.SettingsType.Toggle,
                toggleSettings = new CVRAdvancesAvatarSettingGameObjectToggle
                {
                    defaultValue = true,
                    usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool,
                    useAnimationClip = true,
                    animationClip = on,
                    offAnimationClip = off,
                },
            });
            EditorUtility.SetDirty(avatar);
            return $"{label}: menu toggle \"{label}\" added ({machine}), on and off clips beside the bake; press Create Animator on the CVRAvatar's Advanced Settings to build its layer";
        }

        static AnimationClip Clip(string path, System.Type type, float value, string assetPath)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, assetPath);
            }
            clip.ClearCurves();
            clip.SetCurve(path, type, "material._YAPS_Enabled", AnimationCurve.Constant(0f, 1f / 60f, value));
            EditorUtility.SetDirty(clip);
            return clip;
        }

        static string MachineName(List<CVRAdvancedSettingsEntry> settings, string label)
        {
            string stem = System.Text.RegularExpressions.Regex.Replace(label, "[^a-zA-Z0-9_]+", "");
            if (string.IsNullOrEmpty(stem)) stem = "YAPS";
            string name = stem;
            for (int n = 2; settings.Any(e => e != null && e.machineName == name); n++) name = stem + n;
            return name;
        }

        static string Sanitise(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
#endif
