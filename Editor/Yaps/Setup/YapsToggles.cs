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

        static readonly string[] DefaultSocketNames =
            { "YAPS Hole", "YAPS Ring", "Hole", "Ring", "YAPS Socket", "BakedSpsSocket" };

        // The bone a socket or plug hangs from: its parent, through a YAPS
        // folder and through VRCFury's wrappers, and nothing when that is the
        // avatar itself. A converted socket sits several wrappers deep, and a
        // label built from a wrapper told the wearer nothing about which socket
        // their menu was pointing at.
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
            // The kind is always said: a menu row the wearer cannot tell
            // hole from ring by is a menu row they have to test in game.
            if (string.IsNullOrEmpty(bone)) return $"{socket.name} ({kind})";
            if (DefaultSocketNames.Contains(YapsScanner.StripFuryId(socket.name))) return $"{bone} {kind}";
            return socket.name.Contains(bone)
                ? $"{socket.name} ({kind})"
                : $"{socket.name} ({bone} {kind})";
        }

        // The default-named socket object renamed after its bone, so the
        // Hierarchy reads like the menu. Left alone when anything already
        // animates it by path, and when the user has named it themselves.
        public static string RenameToLabel(YapsSocket socket, CVRAvatar avatar)
        {
            if (socket == null || !DefaultSocketNames.Contains(YapsScanner.StripFuryId(socket.name))) return null;
            // Never a converted socket: the conversion's clips - the
            // lighthouse, the wired toggles - animate paths through it,
            // and ToggledBy only sees clips that switch the object itself.
            if (YapsScanner.StripFuryId(socket.name) == "BakedSpsSocket") return null;
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

            var lighthouse = YapsLighthouse.OwnClips(avatar);
            foreach (var clip in ClipsOfAvatar(avatar, animator))
            {
                // The lighthouse's clips live inside the controller, not in the
                // generated folder, so Generated alone never caught them.
                if (Generated(clip) || lighthouse.Contains(clip)) continue;
                if (Switches(clip, paths, targetPath)) return $"the animation \"{clip.name}\"";
            }
            return null;
        }

        // Every clip any of the avatar's controllers plays, once each.
        //
        // avatar.overrides FIRST, because that is the one ChilloutVR uploads:
        // it was missing here, so a deform already animated from the shipped
        // controller read as not animated at all and YAPS built a toggle
        // against it. The Animator's own slot holds a generated override that
        // is not what ships, and avatarSettings holds the fallback. All of them
        // are read anyway: a clip only has to be reachable to fire.
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
        static bool Switches(AnimationClip clip, HashSet<string> paths, string targetPath)
        {
            if (clip == null) return false;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type == typeof(GameObject) && b.propertyName == "m_IsActive" && paths.Contains(b.path)) return true;
                if (b.path != targetPath) continue;
                if (b.propertyName == "m_Enabled" && typeof(Renderer).IsAssignableFrom(b.type)) return true;
                if (Writes(b, "_YAPS_Enabled")) return true;
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
            // The toolkit's by what it SWITCHES, never by what it is called: the
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

        // Does the AVATAR already drive the deform itself?
        //
        // ToggledBy answers "is the plug's mesh hidden by something", which is
        // a different question and misses the one that matters: an author whose
        // own animation already writes material._YAPS_Enabled from a slider of
        // their own. YAPS then added a menu toggle writing the SAME property
        // from its own layer. Two drivers, one property, and the toggle
        // defaults OFF, so it won and the plug never deformed in play mode or
        // in game. In edit mode no animator ran and the baked 1 stood, which is
        // why it looked like the deform itself had broken.
        //
        // Generated clips are skipped: one is this toggle's own, and finding
        // it would make the toggle stand down for itself.
        static string DrivenByOwnClip(CVRAvatar avatar, string plugPath)
        {
            // Through ClipsOfAvatar, not the Animator's own slot alone. An avatar
            // whose deform is already animated from the shipped controller read as
            // having no controller at all: this returned null, and the toggle was
            // built anyway, straight into the two-drivers-one-property fight above.
            foreach (var clip in ClipsOfAvatar(avatar, avatar.GetComponent<Animator>()))
            {
                if (clip == null || Generated(clip) || !YapsCurveMirror.UserOwned(clip)) continue;
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.path == plugPath && Writes(b, "_YAPS_Enabled"))
                    {
                        return $"\"{clip.name}\", which already animates the deform itself";
                    }
                }
            }
            return null;
        }

        // A menu toggle for a plug's deform: two clips writing _YAPS_Enabled
        // on its material, on and off, as an Advanced Settings toggle with
        // its own animation. Not when anything already hides the plug's
        // mesh: a hidden plug needs no second switch.
        public static string EnsurePlugToggle(YapsPlug plug, CVRAvatar avatar, Material material, string label)
        {
            if (plug == null || avatar == null || material == null || plug.Target == null) return null;
            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            // The toolkit's by the clip it plays, not by its name: the label follows
            // the bone and may have moved since the last build.
            string plugPath = AnimationUtility.CalculateTransformPath(plug.Target.transform, avatar.transform);
            var ours = settings?.FirstOrDefault(e => e != null
                && e.type == CVRAdvancedSettingsEntry.SettingsType.Toggle && e.toggleSettings != null
                && e.toggleSettings.useAnimationClip && Generated(e.toggleSettings.animationClip)
                && AnimationUtility.GetCurveBindings(e.toggleSettings.animationClip)
                    .Any(b => b.path == plugPath && Writes(b, "_YAPS_Enabled")));
            string already = ToggledBy(plug.Target.gameObject, avatar, ignoreEntry: ours != null ? ours.name : label)
                              ?? DrivenByOwnClip(avatar, plugPath);
            if (already != null)
            {
                if (ours == null) return $"{label}: already switched by {already}";
                Undo.RecordObject(avatar, "YAPS toggle");
                settings.Remove(ours);
                NoteRemoved(ours.machineName);
                EditorUtility.SetDirty(avatar);
                return $"{label}: menu toggle removed, the plug is already switched by {already}";
            }
            var targets = Targets(plug, avatar.transform);
            if (ours != null)
            {
                string renamed = Rename(avatar, ours, label);
                // Rewritten in place, so a plug that has since reached more
                // meshes switches all of them.
                Rewrite(ours, targets, "_YAPS_Enabled");
                // The entry is there; its layer may not be, if the animator
                // was never regenerated. Written now, in place.
                string wired = YapsAasAnimator.Wire(avatar, ours);
                return (renamed ?? $"{label}: menu toggle already there") + (wired != null ? "; " + wired : "");
            }

            string dir = YapsNativeBuilder.OutputRoot + "/" + Sanitise(avatar.name);
            YapsNativeBuilder.EnsureFolderPublic(dir);
            var on = Clip(targets, "_YAPS_Enabled", 1f, dir + "/" + Sanitise(label) + " on.anim");
            var off = Clip(targets, "_YAPS_Enabled", 0f, dir + "/" + Sanitise(label) + " off.anim");

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

        // Whether this plug will answer sockets on its own wearer. A separate
        // row from the deform toggle because it answers a different question:
        // not whether the plug bends, but whose sockets it bends toward.
        //
        // OFF by default, and that is the whole reason it exists. A hole ends
        // the shaft, and a socket the wearer is wearing is nearly always
        // nearer to their own plug than anybody else's, so on by default would
        // end the chain at home and never reach the person in front of them.
        //
        // Only written where it can do something. Without the atlas, or on an
        // avatar with no sockets of its own, it would sit in the menu changing
        // nothing, and a dead menu row is worse than no row.
        public static string EnsureSelfToggle(YapsPlug plug, CVRAvatar avatar, Material material, string label)
        {
            if (plug == null || avatar == null || material == null || plug.Target == null) return null;
            if (!material.HasProperty("_YAPS_SelfAllow")) return null;
            if (!material.HasProperty("_YAPS_SelfTag") || material.GetFloat("_YAPS_SelfTag") < 0f) return null;
            if (!material.HasProperty("_YAPS_UseAtlas") || material.GetFloat("_YAPS_UseAtlas") <= 0.5f) return null;

            var settings = avatar.avatarSettings != null ? avatar.avatarSettings.settings : null;
            string plugPath = AnimationUtility.CalculateTransformPath(plug.Target.transform, avatar.transform);
            // Found by the curve it writes, not by its name, the same way the
            // deform toggle is: the label follows the bone and may have moved.
            var ours = settings?.FirstOrDefault(e => e != null
                && e.type == CVRAdvancedSettingsEntry.SettingsType.Toggle && e.toggleSettings != null
                && e.toggleSettings.useAnimationClip && Generated(e.toggleSettings.animationClip)
                && AnimationUtility.GetCurveBindings(e.toggleSettings.animationClip)
                    .Any(b => b.path == plugPath && Writes(b, "_YAPS_SelfAllow")));
            var targets = Targets(plug, avatar.transform);
            if (ours != null)
            {
                string renamed = Rename(avatar, ours, label);
                Rewrite(ours, targets, "_YAPS_SelfAllow");
                // The entry can outlive its layer, if the animator was never
                // regenerated. Written now, in place.
                string wired = YapsAasAnimator.Wire(avatar, ours);
                return (renamed ?? $"{label}: menu toggle already there") + (wired != null ? "; " + wired : "");
            }

            string dir = YapsNativeBuilder.OutputRoot + "/" + Sanitise(avatar.name);
            YapsNativeBuilder.EnsureFolderPublic(dir);
            var on = Clip(targets, "_YAPS_SelfAllow", 1f, dir + "/" + Sanitise(label) + " on.anim");
            var off = Clip(targets, "_YAPS_SelfAllow", 0f, dir + "/" + Sanitise(label) + " off.anim");

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
            return $"{label}: menu toggle \"{label}\" added ({machine}), off by default";
        }

        // A generated on/off pair written again, on every target. Only the
        // toolkit's own clips: an author's are theirs.
        static void Rewrite(CVRAdvancedSettingsEntry entry, List<(string path, Renderer target)> targets, string property)
        {
            var t = entry.toggleSettings;
            if (t == null) return;
            if (Generated(t.animationClip))
                Clip(targets, property, 1f, AssetDatabase.GetAssetPath(t.animationClip));
            if (Generated(t.offAnimationClip))
                Clip(targets, property, 0f, AssetDatabase.GetAssetPath(t.offAnimationClip));
        }

        // One constant curve per renderer whose materials declare the property.
        //
        // "material._X" is the only spelling Unity binds, and it writes the
        // renderer's own property block, which every slot reads. From 4.6.0 a
        // later slot was written as "material[1]._X", on the belief that the
        // plain spelling reached the first material alone. Unity lists no
        // such property and binds nothing to it, so every toggle on a plug
        // whose material was not the mesh's first did nothing.
        //
        // A renderer none of whose slots declares it is skipped: a curve no
        // material reads shows in the animation window as a missing curve,
        // on every avatar that has one, and the author cannot act on it.
        // The plug's own renderer is written anyway, so a plug whose material
        // is assigned after the clip behaves as before.
        //
        // Every mesh of a plug, too: one spanning several renderers kept the
        // others bending with the deform switched off, and answering sockets
        // the rest of it had been told to leave. Slot 0 is the fallback on
        // the plug's own renderer only, never on a mesh it merely reached.
        public static AnimationClip Clip(string path, Renderer target, string property, float value, string assetPath) =>
            Clip(new List<(string, Renderer)> { (path, target) }, property, new Vector4(value, 0, 0, 0), 1, assetPath);

        // The same, for a vector property: four curves per slot, one per
        // component. Unity has no single binding for a Vector4, so ".x" and
        // its three siblings are the only way an animation reaches one.
        public static AnimationClip Clip(string path, Renderer target, string property, Vector4 value, string assetPath) =>
            Clip(new List<(string, Renderer)> { (path, target) }, property, value, 4, assetPath);

        public static AnimationClip Clip(List<(string path, Renderer target)> targets, string property, float value,
            string assetPath) => Clip(targets, property, new Vector4(value, 0, 0, 0), 1, assetPath);

        public static AnimationClip Clip(List<(string path, Renderer target)> targets, string property, Vector4 value,
            string assetPath) => Clip(targets, property, value, 4, assetPath);

        static AnimationClip Clip(List<(string path, Renderer target)> targets, string property, Vector4 value,
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
                if (target == null || SlotsWith(target, property, fallback: t == 0).Count == 0) continue;
                for (int a = 0; a < components; a++)
                {
                    clip.SetCurve(path, target.GetType(), Bound(property) + (components > 1 ? axes[a] : ""),
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

        // Every YAPS material curve the avatar can play that binds to
        // nothing, as "clip: path property". Unity resolves a binding against
        // the hierarchy; one it cannot resolve does nothing in game either,
        // and nothing else says so: toggles on a later material slot were
        // dead from 4.6.0 to 4.6.2 behind clean reports.
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

        // "material[2]._X" read as "material._X". Builds from 4.6.0 wrote the
        // index for later slots, dead as it was, so anything hunting for a
        // property has two spellings to match, and until then matched the
        // first: a toggle on a second slot could be built twice and was
        // never taken away again.
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
