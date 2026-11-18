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
        // Entries added or removed, by machine name, with the count and avatar
        // each was noted at, so a build writes or removes their layers.
        public static int Edits { get; private set; }
        static readonly List<(int at, CVRAvatar avatar, string machine)> _added = new List<(int, CVRAvatar, string)>();
        static readonly List<(int at, CVRAvatar avatar, string machine)> _removed = new List<(int, CVRAvatar, string)>();

        static void NoteAdded(string machineName, CVRAvatar avatar)
        {
            Edits++;
            if (!string.IsNullOrEmpty(machineName)) _added.Add((Edits, avatar, machineName));
        }

        // Without an avatar the name goes to the one the refresh is given.
        public static void NoteRemoved(string machineName, CVRAvatar avatar = null)
        {
            Edits++;
            if (!string.IsNullOrEmpty(machineName)) _removed.Add((Edits, avatar, machineName));
        }

        // Puts the layers of entries added since `editsBefore` into the
        // animator the avatar wears, and takes removed ones out. Returns
        // the note, or null when nothing changed.
        public static string RefreshMenuAnimator(CVRAvatar avatar, int editsBefore)
        {
            // Only names since editsBefore, queue emptied on every return, or
            // leftovers were unwired from the next avatar refreshed.
            var added = _added.Where(n => n.at > editsBefore).ToList();
            var removed = _removed.Where(n => n.at > editsBefore).ToList();
            _added.Clear();
            _removed.Clear();
            if (Edits == editsBefore) return null;
            var notes = new List<string>();
            // On the avatar each was noted on: a null avatar (inactive) must not
            // drop names, or layers stay unwritten or orphaned.
            CVRAvatar On(CVRAvatar noted) => ReferenceEquals(noted, null) ? avatar : noted;
            foreach (var (_, noted, machine) in added)
            {
                var on = On(noted);
                if (on == null) continue;
                var settings = on.avatarSettings != null ? on.avatarSettings.settings : null;
                var entry = settings?.FirstOrDefault(e => e != null && e.machineName == machine);
                if (entry == null) continue;
                string note = YapsAasAnimator.Wire(on, entry);
                if (note != null) notes.Add(note);
            }
            foreach (var (_, noted, machine) in removed)
            {
                var on = On(noted);
                if (on == null) continue;
                string note = YapsAasAnimator.Unwire(on, machine);
                if (note != null) notes.Add(note);
            }
            return notes.Count > 0 ? string.Join("; ", notes) : null;
        }

        // --- names ------------------------------------------------------------
        // Named after the bone, so a menu of a dozen sockets reads.

        static readonly string[] DefaultSocketNames =
            { "YAPS Hole", "YAPS Ring", "Hole", "Ring", "YAPS Socket", "BakedSpsSocket" };

        // The parent bone, through YAPS folders and VRCFury wrappers (a
        // wrapper's name tells the wearer nothing); none when it is the avatar.
        public static string BoneOf(Transform t, CVRAvatar avatar)
        {
            var at = t != null ? t.parent : null;
            while (at != null && (at.name == "YAPS" || at.name == "YAPS Sockets"
                   || at.name == "Original Object" || DefaultSocketNames.Contains(YapsScanner.StripFuryId(at.name))))
                at = at.parent;
            if (at == null) return null;
            if (avatar != null && at == avatar.transform) return null;
            if (at.parent == null) return null;
            return at.name;
        }

        public static string LabelFor(YapsSocket socket)
        {
            if (socket == null) return "YAPS socket";
            // Fury's numbering is noise; "Blowjob" is the author's word
            // for it and the one the wearer knows.
            string bone = YapsScanner.StripFuryId(
                BoneOf(socket.transform, socket.GetComponentInParent<CVRAvatar>(true)));
            string kind = socket.kind == YapsSocket.SocketKind.Hole ? "hole" : "ring";
            // Always say the kind, or the wearer tests it in game.
            if (string.IsNullOrEmpty(bone)) return $"{socket.name} ({kind})";
            if (DefaultSocketNames.Contains(YapsScanner.StripFuryId(socket.name))) return $"{bone} {kind}";
            if (!socket.name.Contains(bone)) return $"{socket.name} ({bone} {kind})";
            // Already says both (RenameToLabel), or it reads "Hips hole (hole)".
            return socket.name.EndsWith(" " + kind, System.StringComparison.OrdinalIgnoreCase)
                ? socket.name
                : $"{socket.name} ({kind})";
        }

        // The default-named socket object renamed after its bone, so the
        // Hierarchy reads like the menu. Left alone when anything already
        // animates it by path, and when the user has named it themselves.
        public static string RenameToLabel(YapsSocket socket, CVRAvatar avatar)
        {
            if (socket == null) return null;
            string bare = YapsScanner.StripFuryId(socket.name);
            if (!DefaultSocketNames.Contains(bare)) return null;
            // Never a converted socket ("YAPS Socket"): conversion clips
            // animate paths through it, which ToggledBy cannot see.
            if (bare == "BakedSpsSocket" || bare == "YAPS Socket") return null;
            string label = LabelFor(socket);
            if (label == socket.name) return null;
            if (ToggledBy(socket.gameObject, avatar) != null) return null;
            string was = socket.name;
            Undo.RecordObject(socket.gameObject, "YAPS socket name");
            socket.gameObject.name = label;
            EditorUtility.SetDirty(socket.gameObject);
            return $"renamed \"{was}\" to \"{label}\"";
        }

        static readonly string[] DefaultPlugNames =
            { "YAPS Plug", "BakedSpsPlug", "SPS Plug", "SPS", "Plug" };

        // A converted plug's root is always "YAPS Plug", so a root label made
        // every row identical.
        static Transform Owner(YapsPlug plug)
        {
            var at = plug.rootBone != null ? plug.rootBone : plug.transform;
            while (at.parent != null && (at.name == "Original Object"
                   || DefaultPlugNames.Contains(YapsScanner.StripFuryId(at.name))))
                at = at.parent;
            return at;
        }

        static YapsPlug[] PlugsBeside(YapsPlug plug)
        {
            var avatar = plug.GetComponentInParent<CVRAvatar>(true);
            return (avatar != null ? avatar.transform : plug.transform.root).GetComponentsInChildren<YapsPlug>(true);
        }

        static string NameOf(YapsPlug plug, YapsPlug[] all)
        {
            var at = Owner(plug);
            string name = YapsScanner.StripFuryId(at.name);
            // One name on two plugs, a prop in each hand: the first wrapper
            // the other does not have tells them apart.
            var twins = all.Where(q => q != null && q != plug && YapsScanner.StripFuryId(Owner(q).name) == name).ToList();
            if (twins.Count == 0) return name;
            var theirs = new HashSet<string>();
            foreach (var q in twins)
                for (var up = Owner(q).parent; up != null; up = up.parent) theirs.Add(YapsScanner.StripFuryId(up.name));
            for (var up = at.parent; up != null && up.parent != null; up = up.parent)
            {
                string n = YapsScanner.StripFuryId(up.name);
                if (!theirs.Contains(n)) return n;
            }
            return name;
        }

        public static string LabelFor(YapsPlug plug)
        {
            if (plug == null) return "YAPS plug";
            return NameOf(plug, PlugsBeside(plug)) + " plug";
        }

        // Each plug's own, on a shared mesh too: the second plug on a mesh
        // switches its own copy of the property, so each has a row.
        public static string MenuLabelFor(YapsPlug plug) => LabelFor(plug);

        // What already switches this object off, by name. Entries, their
        // clips, and any controller clip hiding it or its renderer.
        public static string ToggledBy(GameObject target, CVRAvatar avatar, string ignoreEntry = null, string deform = "_YAPS_Enabled")
        {
            if (target == null) return null;
            var chain = new List<Transform>();
            for (var at = target.transform; at != null; at = at.parent)
            {
                chain.Add(at);
                if (avatar != null && at == avatar.transform) break;
            }
            var chainObjects = new HashSet<GameObject>(chain.Select(t => t.gameObject));

            var animator = avatar != null ? avatar.GetComponent<Animator>() : target.GetComponentInParent<Animator>(true);
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
                        if (t.useAnimationClip && (Switches(t.animationClip, paths, targetPath, deform) || Switches(t.offAnimationClip, paths, targetPath, deform)))
                            return $"the setting \"{entry.name}\"";
                    }
                    if (entry.type == CVRAdvancedSettingsEntry.SettingsType.Dropdown && entry.dropDownSettings != null)
                    {
                        foreach (var option in entry.dropDownSettings.options)
                        {
                            if (option == null) continue;
                            if (option.gameObjectTargets != null && option.gameObjectTargets.Any(g => g != null && g.gameObject != null && chainObjects.Contains(g.gameObject)))
                                return $"the setting \"{entry.name}\"";
                            if (option.useAnimationClip && Switches(option.animationClip, paths, targetPath, deform))
                                return $"the setting \"{entry.name}\"";
                        }
                    }
                }
            }

            var lighthouse = YapsLighthouse.OwnClips(avatar);
            foreach (var clip in ClipsOfAvatar(avatar, animator))
            {
                // The lighthouse's clips live in the controller, not the generated folder.
                if (Generated(clip) || lighthouse.Contains(clip)) continue;
                if (Switches(clip, paths, targetPath, deform)) return $"the animation \"{clip.name}\"";
            }
            return null;
        }

        // avatar.overrides FIRST, the one ChilloutVR uploads; then the fallback
        // and the Animator's generated slot. All are read: a clip only has to
        // be reachable to fire.
        static IEnumerable<AnimationClip> ClipsOfAvatar(CVRAvatar avatar, Animator animator)
        {
            var seen = new HashSet<AnimationClip>();
            var controllers = new List<RuntimeAnimatorController>();
            if (avatar != null) controllers.Add(avatar.overrides);
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
        static bool Switches(AnimationClip clip, HashSet<string> paths, string targetPath, string deform)
        {
            if (clip == null) return false;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type == typeof(GameObject) && b.propertyName == "m_IsActive" && paths.Contains(b.path)) return true;
                if (b.path != targetPath) continue;
                if (b.propertyName == "m_Enabled" && typeof(Renderer).IsAssignableFrom(b.type)) return true;
                if (Writes(b, deform)) return true;
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
                NoteAdded(entry.machineName, avatar);
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
            // By what it SWITCHES, not its name: the label follows the bone, so
            // name matching would add a second entry.
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
                NoteRemoved(ours.machineName, avatar);
                EditorUtility.SetDirty(avatar);
                return $"{label}: menu toggle removed, it is already switched by {already}";
            }
            if (ours != null)
            {
                string renamed = Rename(avatar, ours, label);
                // Off by default, or every socket opened at once in game.
                string off = null;
                if (ours.toggleSettings.defaultValue)
                {
                    Undo.RecordObject(avatar, "YAPS toggle");
                    ours.toggleSettings.defaultValue = false;
                    EditorUtility.SetDirty(avatar);
                    off = "now off by default";
                }
                // The entry is there; its layer may not be, if the animator
                // was never regenerated. Written now, in place.
                string wired = YapsAasAnimator.Wire(avatar, ours);
                return (renamed ?? $"{label}: menu toggle already there") + (off != null ? "; " + off : "")
                       + (wired != null ? "; " + wired : "");
            }
            if (avatar.avatarSettings == null)
            {
                avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
            }
            avatar.avatarUsesAdvancedSettings = true;
            settings = avatar.avatarSettings.settings;
            string machine = MachineName(settings, label);
            Undo.RecordObject(avatar, "YAPS toggle");
            NoteAdded(machine, avatar);
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

        // Does the AVATAR already write material._YAPS_Enabled? A second toggle
        // on the same property, defaulting OFF, won and the plug never deformed
        // in game (edit mode looked fine). Generated clips are skipped, or the
        // toggle stands down for itself.
        static string DrivenByOwnClip(CVRAvatar avatar, string plugPath, string deform)
        {
            // ClipsOfAvatar, not the Animator's slot alone, which missed the
            // shipped controller.
            foreach (var clip in ClipsOfAvatar(avatar, avatar.GetComponent<Animator>()))
            {
                if (clip == null || Generated(clip) || !YapsCurveMirror.UserOwned(clip)) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.path == plugPath && Writes(b, deform))
                    {
                        return $"\"{clip.name}\", which already animates the deform itself";
                    }
                }
            }
            return null;
        }

        // Not when something already hides the plug's mesh.
        public static string EnsurePlugToggle(YapsPlug plug, CVRAvatar avatar, Material material, string label)
        {
            if (plug == null || avatar == null || material == null || plug.Target == null) return null;
            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            // The toolkit's by the clip it plays, not by its name: the label follows
            // the bone and may have moved since the last build.
            string plugPath = AnimationUtility.CalculateTransformPath(plug.Target.transform, avatar.transform);
            System.Func<Renderer, string> deform = r => YapsBaker.SwitchName("_YAPS_Enabled", LaneOn(plug, r));
            var ours = Ours(settings, plugPath, "_YAPS_Enabled", deform(plug.Target), label);
            string already = ToggledBy(plug.Target.gameObject, avatar, ignoreEntry: ours != null ? ours.name : label, deform: deform(plug.Target))
                              ?? DrivenByOwnClip(avatar, plugPath, deform(plug.Target));
            if (already != null)
            {
                if (ours == null) return $"{label}: already switched by {already}";
                Undo.RecordObject(avatar, "YAPS toggle");
                settings.Remove(ours);
                NoteRemoved(ours.machineName, avatar);
                EditorUtility.SetDirty(avatar);
                return $"{label}: menu toggle removed, the plug is already switched by {already}";
            }
            var targets = Targets(plug, avatar.transform);
            if (ours != null)
            {
                string renamed = Rename(avatar, ours, label);
                // Rewritten in place, so a plug that has since reached more
                // meshes switches all of them.
                Rewrite(ours, targets, deform);
                // The entry is there; its layer may not be, if the animator
                // was never regenerated. Written now, in place.
                string wired = YapsAasAnimator.Wire(avatar, ours);
                return (renamed ?? $"{label}: menu toggle already there") + (wired != null ? "; " + wired : "");
            }

            string dir = YapsNativeBuilder.OutputRoot + "/" + Sanitise(avatar.name);
            YapsNativeBuilder.EnsureFolderPublic(dir);
            var on = Clip(targets, deform, 1f, Free(dir + "/" + Sanitise(label) + " on.anim"));
            var off = Clip(targets, deform, 0f, Free(dir + "/" + Sanitise(label) + " off.anim"));

            if (avatar.avatarSettings == null)
            {
                avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
            }
            avatar.avatarUsesAdvancedSettings = true;
            settings = avatar.avatarSettings.settings;
            string machine = MachineName(settings, label);
            Undo.RecordObject(avatar, "YAPS toggle");
            NoteAdded(machine, avatar);
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

        // Whether this plug answers its own wearer's sockets. OFF by default:
        // the wearer's own socket is nearly always nearest, so the chain would
        // end at home. Only written with the atlas and own sockets; otherwise
        // it is a dead row.
        public static string EnsureSelfToggle(YapsPlug plug, CVRAvatar avatar, Material material, string label)
        {
            if (plug == null || avatar == null || material == null || plug.Target == null) return null;

            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            string plugPath = AnimationUtility.CalculateTransformPath(plug.Target.transform, avatar.transform);
            // Found by the curve it writes, not by its name, the same way the
            // deform toggle is: the label follows the bone and may have moved.
            System.Func<Renderer, string> self = r => YapsBaker.SwitchName("_YAPS_SelfAllow", LaneOn(plug, r));
            var ours = Ours(settings, plugPath, "_YAPS_SelfAllow", self(plug.Target), label);
            bool applies = material.HasProperty("_YAPS_SelfAllow")
                           && material.HasProperty("_YAPS_SelfTag") && material.GetFloat("_YAPS_SelfTag") >= 0f
                           && material.HasProperty("_YAPS_UseAtlas") && material.GetFloat("_YAPS_UseAtlas") > 0.5f;
            if (!applies)
            {
                if (ours == null) return null;
                // An earlier build's row, once the sockets or the atlas went.
                Undo.RecordObject(avatar, "YAPS toggle");
                settings.Remove(ours);
                NoteRemoved(ours.machineName, avatar);
                EditorUtility.SetDirty(avatar);
                return $"{label}: menu toggle removed, it can no longer change anything";
            }
            var targets = Targets(plug, avatar.transform);
            if (ours != null)
            {
                string renamed = Rename(avatar, ours, label);
                Rewrite(ours, targets, self);
                // The entry can outlive its layer, if the animator was never
                // regenerated. Written now, in place.
                string wired = YapsAasAnimator.Wire(avatar, ours);
                return (renamed ?? $"{label}: menu toggle already there") + (wired != null ? "; " + wired : "");
            }

            string dir = YapsNativeBuilder.OutputRoot + "/" + Sanitise(avatar.name);
            YapsNativeBuilder.EnsureFolderPublic(dir);
            var on = Clip(targets, self, 1f, Free(dir + "/" + Sanitise(label) + " on.anim"));
            var off = Clip(targets, self, 0f, Free(dir + "/" + Sanitise(label) + " off.anim"));

            if (avatar.avatarSettings == null)
            {
                avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
            }
            avatar.avatarUsesAdvancedSettings = true;
            settings = avatar.avatarSettings.settings;
            string machine = MachineName(settings, label);
            Undo.RecordObject(avatar, "YAPS toggle");
            NoteAdded(machine, avatar);
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
            return $"{label}: menu toggle \"{label}\" added ({machine}), off by default";
        }

        // Label first: older builds wrote one shared switch for two plugs, so
        // the property alone would hand one plug the other's row.
        static CVRAdvancedSettingsEntry Ours(List<CVRAdvancedSettingsEntry> settings, string plugPath, string name, string own, string label)
        {
            var mine = settings?.Where(e => e != null
                && e.type == CVRAdvancedSettingsEntry.SettingsType.Toggle && e.toggleSettings != null
                && e.toggleSettings.useAnimationClip && Generated(e.toggleSettings.animationClip)).ToList();
            if (mine == null) return null;
            bool At(CVRAdvancedSettingsEntry e, string property) => AnimationUtility.GetCurveBindings(e.toggleSettings.animationClip)
                .Any(b => b.path == plugPath && Writes(b, property));
            return mine.FirstOrDefault(e => e.name == label && (At(e, name) || At(e, name + "2")))
                   ?? mine.FirstOrDefault(e => At(e, own));
        }

        // The lane the plug's own material on this mesh was given; 0 where it has none.
        public static int LaneOn(YapsPlug plug, Renderer r)
        {
            var mats = r != null ? r.sharedMaterials : new Material[0];
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && YapsNativeBuilder.OwnsSlot(plug, r, i, mats[i])) return YapsBaker.LaneOf(mats[i]);
            return 0;
        }

        // A generated on/off pair written again, on every target. Only the
        // toolkit's own clips: an author's are theirs.
        static void Rewrite(CVRAdvancedSettingsEntry entry, List<(string path, Renderer target)> targets, System.Func<Renderer, string> property)
        {
            var t = entry.toggleSettings;
            if (t == null) return;
            if (Generated(t.animationClip))
                Clip(targets, property, 1f, AssetDatabase.GetAssetPath(t.animationClip));
            if (Generated(t.offAnimationClip))
                Clip(targets, property, 0f, AssetDatabase.GetAssetPath(t.offAnimationClip));
        }

        // One constant curve per renderer whose materials declare the property.
        // "material._X" is the only spelling Unity binds; it writes the
        // property block every slot reads ("material[1]._X" binds nothing).
        // Renderers that never declare it are skipped (a missing curve the
        // author cannot fix), except the plug's own. Every mesh of the plug,
        // or the others keep bending when switched off. Slot 0 fallback on
        // the plug's own renderer only.
        public static AnimationClip Clip(string path, Renderer target, string property, float value, string assetPath) =>
            Clip(new List<(string, Renderer)> { (path, target) }, _ => property, new Vector4(value, 0, 0, 0), 1, assetPath);

        // A Vector4 needs four curves: Unity has no single binding for one.
        public static AnimationClip Clip(string path, Renderer target, string property, Vector4 value, string assetPath) =>
            Clip(new List<(string, Renderer)> { (path, target) }, _ => property, value, 4, assetPath);

        public static AnimationClip Clip(List<(string path, Renderer target)> targets, string property, float value,
            string assetPath) => Clip(targets, _ => property, new Vector4(value, 0, 0, 0), 1, assetPath);

        // A name per mesh: a plug's own switch is the second plug's copy on a mesh where it is second.
        static AnimationClip Clip(List<(string path, Renderer target)> targets, System.Func<Renderer, string> property, float value,
            string assetPath) => Clip(targets, property, new Vector4(value, 0, 0, 0), 1, assetPath);

        public static AnimationClip Clip(List<(string path, Renderer target)> targets, string property, Vector4 value,
            string assetPath) => Clip(targets, _ => property, value, 4, assetPath);

        static AnimationClip Clip(List<(string path, Renderer target)> targets, System.Func<Renderer, string> property, Vector4 value,
            int components, string assetPath)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, assetPath);
            }
            clip.ClearCurves();
            string[] axes = { ".x", ".y", ".z", ".w" };
            for (int t = 0; t < targets.Count; t++)
            {
                var (path, target) = targets[t];
                if (target == null || SlotsWith(target, property(target), fallback: t == 0).Count == 0) continue;
                for (int a = 0; a < components; a++)
                {
                    clip.SetCurve(path, target.GetType(), Bound(property(target)) + (components > 1 ? axes[a] : ""),
                        AnimationCurve.Constant(0f, 1f / 60f, value[a]));
                }
            }
            EditorUtility.SetDirty(clip);
            return clip;
        }

        // The slots of a renderer whose material declares a property; slot 0
        // alone when none does and a fallback is wanted.
        public static List<int> SlotsWith(Renderer target, string property, bool fallback = true)
        {
            var slots = new List<int>();
            var mats = target != null ? target.sharedMaterials : new Material[0];
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].HasProperty(property)) slots.Add(i);
            }
            if (slots.Count == 0 && fallback) slots.Add(0);
            return slots;
        }

        // Every renderer a plug's bake reached, its own first.
        public static List<Renderer> MeshesOf(YapsPlug plug)
        {
            var meshes = new List<Renderer>();
            if (plug == null) return meshes;
            if (plug.Target != null) meshes.Add(plug.Target);
            foreach (var b in plug.bakedSlots)
            {
                if (b != null && b.renderer != null && !meshes.Contains(b.renderer)) meshes.Add(b.renderer);
            }
            return meshes;
        }

        // The same with each one's path, for a clip to write on all of them.
        public static List<(string path, Renderer target)> Targets(YapsPlug plug, Transform root) =>
            MeshesOf(plug).Select(r => (AnimationUtility.CalculateTransformPath(r.transform, root), r)).ToList();

        // How Unity spells a material property, for every slot at once.
        public static string Bound(string property)
        {
            return "material." + property;
        }

        // Every YAPS material curve that binds to nothing, as "clip: path
        // property". Unbound curves do nothing in game and nothing else says so.
        public static List<string> DeadBindings(GameObject root, UnityEditor.Animations.AnimatorController controller)
        {
            var dead = new List<string>();
            if (root == null) return dead;
            var clips = new List<AnimationClip>();
            if (controller != null) clips.AddRange(controller.animationClips);
            var avatar = root.GetComponent<CVRAvatar>();
            var settings = avatar != null && avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            if (settings != null)
            {
                foreach (var e in settings)
                {
                    if (e?.toggleSettings == null || !e.toggleSettings.useAnimationClip) continue;
                    clips.Add(e.toggleSettings.animationClip);
                    clips.Add(e.toggleSettings.offAnimationClip);
                }
            }
            foreach (var clip in clips.Where(c => c != null).Distinct())
            {
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!Bare(b.propertyName).StartsWith("material._YAPS_", System.StringComparison.Ordinal)) continue;
                    if (AnimationUtility.GetEditorCurveValueType(root, b) == null)
                        dead.Add(clip.name + ": " + b.path + " " + b.propertyName);
                }
            }
            return dead;
        }

        // "material[2]._X" read as "material._X": older builds wrote the dead
        // indexed spelling, so lookups match both.
        public static string Bare(string propertyName)
        {
            if (propertyName == null
                || !propertyName.StartsWith("material[", System.StringComparison.Ordinal)) return propertyName;
            int close = propertyName.IndexOf(']');
            return close < 0 ? propertyName : "material" + propertyName.Substring(close + 1);
        }

        // Whether a binding writes this shader property, on any slot.
        public static bool Writes(EditorCurveBinding b, string property)
        {
            return Bare(b.propertyName) == "material." + property;
        }

        // Unique among entries; `mine` is excluded so a rename keeps its own name.
        static string MachineName(List<CVRAdvancedSettingsEntry> settings, string label,
            CVRAdvancedSettingsEntry mine = null)
        {
            // "Chest ring" becomes ChestRing.
            var words = System.Text.RegularExpressions.Regex.Split(label ?? "", "[^a-zA-Z0-9_]+")
                .Where(w => w.Length > 0)
                .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1));
            string stem = string.Concat(words);
            if (string.IsNullOrEmpty(stem)) stem = "YAPS";
            string name = stem;
            for (int n = 2; settings.Any(e => e != null && e != mine && e.machineName == name); n++) name = stem + n;
            return name;
        }

        // A new entry's clip path, unless a toggle already plays the file there.
        // The name is the avatar's and the label's, so a second avatar of one
        // name, or an entry renamed away from its label, would be rewritten.
        // ponytail: scene avatars only; a prefab off the scene is not asked.
        static string Free(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) return path;
            bool played = Object.FindObjectsOfType<CVRAvatar>(true).Any(a => a.avatarSettings?.settings != null
                && a.avatarSettings.settings.Any(e => e?.toggleSettings != null
                    && (e.toggleSettings.animationClip == clip || e.toggleSettings.offAnimationClip == clip)));
            return played ? AssetDatabase.GenerateUniqueAssetPath(path) : path;
        }

        static string Sanitise(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
#endif
