// A socket nobody can switch off holds a light slot forever, and a plug
// with no switch cannot be put away. When nothing on the avatar already
// toggles one, Build gives it an Advanced Settings entry the CCK turns
// into a menu toggle and an animator layer of its own, and the toolkit
// regenerates the menu animator itself, since a line telling the user to
// press Create Animator went unread.
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
        // How many entries the toolkit added or removed, so a build knows
        // whether the menu animator needs regenerating; and the machine
        // names of removed entries, whose layers go with them.
        public static int Edits { get; private set; }
        static readonly List<string> _removed = new List<string>();

        public static void NoteRemoved(string machineName)
        {
            Edits++;
            if (!string.IsNullOrEmpty(machineName)) _removed.Add(machineName);
        }

        public static List<string> TakeRemoved()
        {
            var list = new List<string>(_removed);
            _removed.Clear();
            return list;
        }

        // Regenerates the avatar's menu animator when entries changed since
        // `editsBefore`; the CCK's Create Animator, run for the user.
        // Returns the note, or null when nothing changed.
        public static string RefreshMenuAnimator(CVRAvatar avatar, int editsBefore)
        {
            if (avatar == null || Edits == editsBefore) return null;
            YapsAasAnimator.Regenerate(avatar, TakeRemoved(), out string note);
            return note;
        }

        // What already switches this object, by name; null when nothing does.
        // An Advanced Settings entry aiming at it or an ancestor, an entry
        // whose own clips do, or a clip in any of the avatar's controllers
        // that drives m_IsActive on it or an ancestor, or its renderer's
        // m_Enabled. Clips the toolkit generated do not count, nor does the
        // entry named in `ignoreEntry`: those are the toolkit's own toggle,
        // and the question is whether anything ELSE switches it.
        public static string ToggledBy(GameObject target, CVRAvatar avatar, string ignoreEntry = null)
        {
            if (target == null) return null;
            var chain = new List<Transform>();
            for (var at = target.transform; at != null; at = at.parent)
            {
                chain.Add(at);
                if (avatar != null && at == avatar.transform) break;
            }
            var chainObjects = new HashSet<GameObject>(chain.Select(t => t.gameObject));

            var animator = avatar != null ? avatar.GetComponent<Animator>() : target.GetComponentInParent<Animator>();
            var root = animator != null ? animator.transform : (avatar != null ? avatar.transform : target.transform.root);
            var paths = new HashSet<string>(chain.Select(t => AnimationUtility.CalculateTransformPath(t, root)));
            string targetPath = AnimationUtility.CalculateTransformPath(target.transform, root);

            if (avatar != null && avatar.avatarSettings != null && avatar.avatarSettings.settings != null)
            {
                foreach (var entry in avatar.avatarSettings.settings)
                {
                    if (entry == null || entry.name == ignoreEntry) continue;
                    if (entry.type == CVRAdvancedSettingsEntry.SettingsType.Toggle && entry.toggleSettings != null)
                    {
                        var t = entry.toggleSettings;
                        if (t.gameObjectTargets != null && t.gameObjectTargets.Any(g => g != null && g.gameObject != null && chainObjects.Contains(g.gameObject)))
                            return $"the setting \"{entry.name}\"";
                        if (t.useAnimationClip && (Switches(t.animationClip, paths, targetPath) || Switches(t.offAnimationClip, paths, targetPath)))
                            return $"the setting \"{entry.name}\"";
                    }
                    if (entry.type == CVRAdvancedSettingsEntry.SettingsType.Dropdown && entry.dropDownSettings != null)
                    {
                        foreach (var option in entry.dropDownSettings.options)
                        {
                            if (option == null) continue;
                            if (option.gameObjectTargets != null && option.gameObjectTargets.Any(g => g != null && g.gameObject != null && chainObjects.Contains(g.gameObject)))
                                return $"the setting \"{entry.name}\"";
                            if (option.useAnimationClip && Switches(option.animationClip, paths, targetPath))
                                return $"the setting \"{entry.name}\"";
                        }
                    }
                }
            }

            foreach (var clip in ClipsOfAvatar(avatar, animator))
            {
                if (Generated(clip)) continue;
                if (Switches(clip, paths, targetPath)) return $"the animation \"{clip.name}\"";
            }
            return null;
        }

        // Every clip any of the avatar's controllers plays: the animator's,
        // the CCK's base controller and its override, once each.
        static IEnumerable<AnimationClip> ClipsOfAvatar(CVRAvatar avatar, Animator animator)
        {
            var seen = new HashSet<AnimationClip>();
            var controllers = new List<RuntimeAnimatorController>();
            if (animator != null) controllers.Add(animator.runtimeAnimatorController);
            if (avatar != null && avatar.avatarSettings != null)
            {
                controllers.Add(avatar.avatarSettings.baseController);
                controllers.Add(avatar.avatarSettings.baseOverrideController);
            }
            foreach (var controller in controllers)
            {
                foreach (var clip in YapsCurveMirror.ClipsOf(controller))
                    if (seen.Add(clip)) yield return clip;
            }
        }

        // Does this clip switch the object off: its activity or an
        // ancestor's, its renderer, or a YAPS deform on it.
        static bool Switches(AnimationClip clip, HashSet<string> paths, string targetPath)
        {
            if (clip == null) return false;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type == typeof(GameObject) && b.propertyName == "m_IsActive" && paths.Contains(b.path)) return true;
                if (b.path != targetPath) continue;
                if (b.propertyName == "m_Enabled" && typeof(Renderer).IsAssignableFrom(b.type)) return true;
                if (b.propertyName == "material._YAPS_Enabled") return true;
            }
            return false;
        }

        static bool Generated(AnimationClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            return !string.IsNullOrEmpty(path)
                   && path.Replace('\\', '/').StartsWith(YapsNativeBuilder.OutputRoot + "/", System.StringComparison.OrdinalIgnoreCase);
        }

        // A menu toggle for an object: on and off by activity, default as
        // it stands now. Returns what happened, for the window.
        public static string EnsureObjectToggle(GameObject target, CVRAvatar avatar, string label)
        {
            if (target == null || avatar == null) return null;
            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            var ours = settings?.FirstOrDefault(e => e != null && e.name == label
                && e.type == CVRAdvancedSettingsEntry.SettingsType.Toggle && e.toggleSettings != null
                && e.toggleSettings.gameObjectTargets != null
                && e.toggleSettings.gameObjectTargets.Any(g => g != null && g.gameObject == target));
            string already = ToggledBy(target, avatar, ignoreEntry: label);
            if (already != null)
            {
                if (ours == null) return $"{label}: already switched by {already}";
                // An earlier build gave it a toggle it did not need.
                Undo.RecordObject(avatar, "YAPS toggle");
                settings.Remove(ours);
                NoteRemoved(ours.machineName);
                EditorUtility.SetDirty(avatar);
                return $"{label}: menu toggle removed, it is already switched by {already}";
            }
            if (ours != null) return $"{label}: menu toggle already there";
            if (avatar.avatarSettings == null)
            {
                avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
            }
            avatar.avatarUsesAdvancedSettings = true;
            settings = avatar.avatarSettings.settings;
            string machine = MachineName(settings, label);
            Undo.RecordObject(avatar, "YAPS toggle");
            Edits++;
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
            return $"{label}: menu toggle \"{label}\" added ({machine})";
        }

        // A menu toggle for a plug's deform: two clips writing _YAPS_Enabled
        // on its material, on and off, as an Advanced Settings toggle with
        // its own animation. Not when anything already hides the plug's
        // mesh: a hidden plug needs no second switch.
        public static string EnsurePlugToggle(YapsPlug plug, CVRAvatar avatar, Material material, string label)
        {
            if (plug == null || avatar == null || material == null || plug.Target == null) return null;
            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            var ours = settings?.FirstOrDefault(e => e != null && e.name == label
                && e.type == CVRAdvancedSettingsEntry.SettingsType.Toggle && e.toggleSettings != null
                && e.toggleSettings.useAnimationClip && Generated(e.toggleSettings.animationClip));
            string already = ToggledBy(plug.Target.gameObject, avatar, ignoreEntry: label);
            if (already != null)
            {
                if (ours == null) return $"{label}: already switched by {already}";
                Undo.RecordObject(avatar, "YAPS toggle");
                settings.Remove(ours);
                NoteRemoved(ours.machineName);
                EditorUtility.SetDirty(avatar);
                return $"{label}: menu toggle removed, the plug is already switched by {already}";
            }
            if (ours != null) return $"{label}: menu toggle already there";

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
            settings = avatar.avatarSettings.settings;
            string machine = MachineName(settings, label);
            Undo.RecordObject(avatar, "YAPS toggle");
            Edits++;
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
            return $"{label}: menu toggle \"{label}\" added ({machine}), on and off clips beside the bake";
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
