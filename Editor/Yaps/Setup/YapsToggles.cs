// Menu toggles for a socket or plug nothing else switches, off by
// default. The layer and parameter are written in place, not generated.
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
        // whether the animator needs touching; and which, by machine name,
        // so the layer of an added entry is written and a removed one's
        // taken out.
        public static int Edits { get; private set; }
        static readonly List<string> _added = new List<string>();
        static readonly List<string> _removed = new List<string>();

        static void NoteAdded(string machineName)
        {
            Edits++;
            if (!string.IsNullOrEmpty(machineName)) _added.Add(machineName);
        }

        public static void NoteRemoved(string machineName)
        {
            Edits++;
            if (!string.IsNullOrEmpty(machineName)) _removed.Add(machineName);
        }

        // Puts the layers of entries added since `editsBefore` into the
        // animator the avatar wears, and takes removed ones out. Returns
        // the note, or null when nothing changed.
        public static string RefreshMenuAnimator(CVRAvatar avatar, int editsBefore)
        {
            if (avatar == null || Edits == editsBefore) return null;
            var notes = new List<string>();
            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            foreach (string machine in _added.ToList())
            {
                var entry = settings?.FirstOrDefault(e => e != null && e.machineName == machine);
                if (entry == null) continue;
                string note = YapsAasAnimator.Wire(avatar, entry);
                if (note != null) notes.Add(note);
            }
            foreach (string machine in _removed.ToList())
            {
                string note = YapsAasAnimator.Unwire(avatar, machine);
                if (note != null) notes.Add(note);
            }
            _added.Clear();
            _removed.Clear();
            return notes.Count > 0 ? string.Join("; ", notes) : null;
        }

        // --- names ------------------------------------------------------------
        // Named after the bone, so a menu of a dozen sockets reads.

        static readonly string[] DefaultSocketNames = { "YAPS Hole", "YAPS Ring", "Hole", "Ring", "YAPS Socket" };

        // The bone a socket or plug hangs from: its parent, through a YAPS
        // folder, and nothing when that is the avatar itself.
        public static string BoneOf(Transform t, CVRAvatar avatar)
        {
            var at = t != null ? t.parent : null;
            while (at != null && (at.name == "YAPS" || at.name == "YAPS Sockets")) at = at.parent;
            if (at == null) return null;
            if (avatar != null && at == avatar.transform) return null;
            if (at.parent == null) return null;
            return at.name;
        }

        public static string LabelFor(YapsSocket socket)
        {
            if (socket == null) return "YAPS socket";
            string bone = BoneOf(socket.transform, socket.GetComponentInParent<CVRAvatar>());
            string kind = socket.kind == YapsSocket.SocketKind.Hole ? "hole" : "ring";
            if (bone == null) return socket.name;
            if (DefaultSocketNames.Contains(socket.name)) return $"{bone} {kind}";
            // A name that already says where it is says it once.
            return socket.name.Contains(bone) ? socket.name : $"{socket.name} ({bone})";
        }

        // The default-named socket object renamed after its bone, so the
        // Hierarchy reads like the menu. Left alone when anything already
        // animates it by path, and when the user has named it themselves.
        public static string RenameToLabel(YapsSocket socket, CVRAvatar avatar)
        {
            if (socket == null || !DefaultSocketNames.Contains(socket.name)) return null;
            string label = LabelFor(socket);
            if (label == socket.name) return null;
            if (ToggledBy(socket.gameObject, avatar) != null) return null;
            string was = socket.name;
            Undo.RecordObject(socket.gameObject, "YAPS socket name");
            socket.gameObject.name = label;
            EditorUtility.SetDirty(socket.gameObject);
            return $"renamed \"{was}\" to \"{label}\"";
        }

        public static string LabelFor(YapsPlug plug)
        {
            if (plug == null) return "YAPS plug";
            string bone = plug.rootBone != null ? plug.rootBone.name : plug.name;
            return $"{bone} plug";
        }

        // What already switches this object off, by name. Entries, their
        // clips, and any controller clip hiding it or its renderer.
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

        // An entry whose label has moved on: renamed in place, and its
        // animator layer and parameter renamed with it, since the machine
        // name is what the layer is called. Null when nothing changed.
        static string Rename(CVRAvatar avatar, CVRAdvancedSettingsEntry entry, string label)
        {
            if (entry.name == label) return null;
            string was = entry.name, wasMachine = entry.machineName;
            Undo.RecordObject(avatar, "YAPS toggle");
            entry.name = label;
            entry.machineName = MachineName(avatar.avatarSettings.settings, label, entry);
            EditorUtility.SetDirty(avatar);
            if (entry.machineName != wasMachine)
            {
                YapsAasAnimator.Unwire(avatar, wasMachine);
                NoteAdded(entry.machineName);
            }
            return $"{label}: menu toggle renamed from \"{was}\"" +
                   (entry.machineName != wasMachine ? $" ({wasMachine} → {entry.machineName})" : "");
        }

        // A menu toggle for an object: on and off by activity, default as
        // it stands now. Returns what happened, for the window.
        public static string EnsureObjectToggle(GameObject target, CVRAvatar avatar, string label)
        {
            if (target == null || avatar == null) return null;
            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            // Ours by what it SWITCHES, never by what it is called: the
            // label follows the bone, so renaming the bone or moving the
            // socket renames the entry, and matching on the name would add
            // a second one instead of renaming the first.
            var ours = settings?.FirstOrDefault(e => e != null
                && e.type == CVRAdvancedSettingsEntry.SettingsType.Toggle && e.toggleSettings != null
                && !e.toggleSettings.useAnimationClip
                && e.toggleSettings.gameObjectTargets != null
                && e.toggleSettings.gameObjectTargets.Count > 0
                && e.toggleSettings.gameObjectTargets.All(g => g != null && g.gameObject == target));
            string already = ToggledBy(target, avatar, ignoreEntry: ours != null ? ours.name : label);
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
            if (ours != null)
            {
                string renamed = Rename(avatar, ours, label);
                // The entry is there; its layer may not be, if the animator
                // was never regenerated. Written now, in place.
                string wired = YapsAasAnimator.Wire(avatar, ours);
                return (renamed ?? $"{label}: menu toggle already there") + (wired != null ? "; " + wired : "");
            }
            if (avatar.avatarSettings == null)
            {
                avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
            }
            avatar.avatarUsesAdvancedSettings = true;
            settings = avatar.avatarSettings.settings;
            string machine = MachineName(settings, label);
            Undo.RecordObject(avatar, "YAPS toggle");
            NoteAdded(machine);
            settings.Add(new CVRAdvancedSettingsEntry
            {
                name = label,
                machineName = machine,
                type = CVRAdvancedSettingsEntry.SettingsType.Toggle,
                toggleSettings = new CVRAdvancesAvatarSettingGameObjectToggle
                {
                    defaultValue = false,
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
            // Ours by the clip it plays, not by its name: the label follows
            // the bone and may have moved since the last build.
            string plugPath = AnimationUtility.CalculateTransformPath(plug.Target.transform, avatar.transform);
            var ours = settings?.FirstOrDefault(e => e != null
                && e.type == CVRAdvancedSettingsEntry.SettingsType.Toggle && e.toggleSettings != null
                && e.toggleSettings.useAnimationClip && Generated(e.toggleSettings.animationClip)
                && AnimationUtility.GetCurveBindings(e.toggleSettings.animationClip)
                    .Any(b => b.path == plugPath && b.propertyName == "material._YAPS_Enabled"));
            string already = ToggledBy(plug.Target.gameObject, avatar, ignoreEntry: ours != null ? ours.name : label);
            if (already != null)
            {
                if (ours == null) return $"{label}: already switched by {already}";
                Undo.RecordObject(avatar, "YAPS toggle");
                settings.Remove(ours);
                NoteRemoved(ours.machineName);
                EditorUtility.SetDirty(avatar);
                return $"{label}: menu toggle removed, the plug is already switched by {already}";
            }
            if (ours != null)
            {
                string renamed = Rename(avatar, ours, label);
                // The entry is there; its layer may not be, if the animator
                // was never regenerated. Written now, in place.
                string wired = YapsAasAnimator.Wire(avatar, ours);
                return (renamed ?? $"{label}: menu toggle already there") + (wired != null ? "; " + wired : "");
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
            settings = avatar.avatarSettings.settings;
            string machine = MachineName(settings, label);
            Undo.RecordObject(avatar, "YAPS toggle");
            NoteAdded(machine);
            settings.Add(new CVRAdvancedSettingsEntry
            {
                name = label,
                machineName = machine,
                type = CVRAdvancedSettingsEntry.SettingsType.Toggle,
                toggleSettings = new CVRAdvancesAvatarSettingGameObjectToggle
                {
                    defaultValue = false,
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

        // A parameter name from a label, unique among the entries. `mine`
        // is the entry being named, so a rename does not collide with the
        // name it already holds.
        static string MachineName(List<CVRAdvancedSettingsEntry> settings, string label,
            CVRAdvancedSettingsEntry mine = null)
        {
            // Words joined without spaces, each capitalised: "Chest ring"
            // becomes ChestRing rather than Chestring.
            var words = System.Text.RegularExpressions.Regex.Split(label ?? "", "[^a-zA-Z0-9_]+")
                .Where(w => w.Length > 0)
                .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1));
            string stem = string.Concat(words);
            if (string.IsNullOrEmpty(stem)) stem = "YAPS";
            string name = stem;
            for (int n = 2; settings.Any(e => e != null && e != mine && e.machineName == name); n++) name = stem + n;
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
