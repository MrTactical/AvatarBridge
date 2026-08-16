// Takes a socket or a plug out again, entire: the objects the toolkit
// made, the reactions layer and its parameter, the menu toggle, the size
// wiring, the bake, and the material it replaced back in its slot. Also
// sweeps up what a socket or plug deleted by hand left behind, since a
// layer whose parameter is gone shouts in the console on every reload.
// The scene, the controllers and the avatar's settings go in one undo
// step; the generated files stay where they are and come back with the
// next Bake, so an undo never points at a missing asset.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsRemover
    {
        // Objects the toolkit puts under a plug or a socket, by name.
        static readonly string[] OwnChildren = { "YAPS Markers", "YAPS Depth", "YAPS Lights", "YAPS Pointers" };

        // The material curves Bake writes into the avatar's own clips.
        static readonly string[] WiredProperties =
        {
            "material._YAPS_BakeScale", "material._YAPS_BakeGirth",
            "material._YAPS_ShapeWeights.x", "material._YAPS_ShapeWeights.y", "material._YAPS_ShapeWeights.z", "material._YAPS_ShapeWeights.w",
            "material._YAPS_ShapeWeights2.x", "material._YAPS_ShapeWeights2.y", "material._YAPS_ShapeWeights2.z", "material._YAPS_ShapeWeights2.w",
            "material._YAPS_ShapeWeights3.x", "material._YAPS_ShapeWeights3.y", "material._YAPS_ShapeWeights3.z", "material._YAPS_ShapeWeights3.w",
            "material._YAPS_ShapeWeights4.x", "material._YAPS_ShapeWeights4.y", "material._YAPS_ShapeWeights4.z", "material._YAPS_ShapeWeights4.w",
        };

        // What Remove would do, for the confirmation, one line each.
        public static List<string> Plan(YapsSocket socket)
        {
            var lines = new List<string>();
            if (socket == null) return lines;
            var avatar = socket.GetComponentInParent<CVRAvatar>();
            lines.Add(OwnObject(socket.gameObject, typeof(YapsSocket))
                ? $"the object \"{socket.name}\" and everything under it"
                : $"the YAPS Socket component on \"{socket.name}\" and the markers, lights and pointers under it (the object stays: it has other things on it)");
            if (YapsSocketReactions.Exists(socket))
                lines.Add($"the animator layer \"{YapsSocketReactions.LayerName(socket)}\" and its parameter");
            if (avatar != null && ToggleEntriesFor(avatar, socket.gameObject).Any())
                lines.Add($"the menu toggle \"{socket.name}\"");
            if (socket.renderer != null && socket.bakedFrom != null)
                lines.Add($"the socket bake on \"{socket.renderer.name}\": its material goes back to \"{socket.bakedFrom.name}\"");
            return lines;
        }

        public static List<string> Plan(YapsPlug plug)
        {
            var lines = new List<string>();
            if (plug == null) return lines;
            var avatar = plug.GetComponentInParent<CVRAvatar>();
            var renderer = plug.Target;
            lines.Add(OwnObject(plug.gameObject, typeof(YapsPlug))
                ? $"the object \"{plug.name}\" and everything under it"
                : $"the YAPS Plug component on \"{plug.name}\" and its markers (the object stays: it is a bone or a mesh of yours)");
            if (renderer != null && BakedSlots(renderer).Any())
            {
                var back = OriginalMaterial(plug, renderer, out string how);
                lines.Add(back != null
                    ? $"the bake on \"{renderer.name}\": its material goes back to \"{back.name}\" ({how})"
                    : $"the bake on \"{renderer.name}\": the deform is switched off ({how})");
            }
            if (avatar != null && renderer != null && WiredClips(avatar, renderer).Any())
                lines.Add("the size wiring Bake added to your own animations");
            if (avatar != null && ToggleEntriesFor(avatar, plug).Any())
                lines.Add($"the menu toggle \"{plug.name} YAPS\"");
            return lines;
        }

        // Asks, listing what goes, then removes. True when it did.
        public static bool Ask(YapsSocket socket)
        {
            if (socket == null) return false;
            if (!Confirm($"Remove socket \"{socket.name}\"?", Plan(socket))) return false;
            Debug.Log("[YAPS] " + RemoveSocket(socket));
            return true;
        }

        public static bool Ask(YapsPlug plug)
        {
            if (plug == null) return false;
            if (!Confirm($"Remove plug \"{plug.name}\"?", Plan(plug))) return false;
            Debug.Log("[YAPS] " + RemovePlug(plug));
            return true;
        }

        static bool Confirm(string title, List<string> plan)
        {
            return EditorUtility.DisplayDialog(title,
                "This removes:\n•  " + string.Join("\n•  ", plan) +
                "\n\nOne undo step. The files it generated stay in " + YapsNativeBuilder.OutputRoot + " for the next Bake.",
                "Remove", "Keep");
        }

        // --- sockets ---------------------------------------------------------

        public static string RemoveSocket(YapsSocket socket)
        {
            if (socket == null) return null;
            string name = socket.name;
            var avatar = socket.GetComponentInParent<CVRAvatar>();
            var top = TopOf(socket.transform);
            var done = new List<string>();

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Remove YAPS socket " + name);

            YapsPreview.Set(socket, false);
            YapsShapeSim.Release(socket);

            // The reactions layer and its parameter, in every controller.
            string layer = YapsSocketReactions.LayerName(socket);
            string parameter = YapsSocketReactions.Parameter(socket);
            int layers = RemoveLayer(ControllersOf(top), layer, parameter);
            if (layers > 0) done.Add($"layer \"{layer}\" out of {layers} controller(s)");

            // The socket's toggle, and the menu animator without it.
            if (avatar != null)
            {
                int before = YapsToggles.Edits;
                int entries = RemoveEntries(avatar, ToggleEntriesFor(avatar, socket.gameObject));
                if (entries > 0) done.Add("its menu toggle");
                string menu = YapsToggles.RefreshMenuAnimator(avatar, before);
                if (menu != null) done.Add(menu.TrimEnd('.'));
            }

            // The bake on its own mesh: the material it replaced goes back.
            if (socket.renderer != null && socket.bakedFrom != null)
            {
                var mats = socket.renderer.sharedMaterials;
                if (mats.Length > 0 && mats[0] != null && mats[0].HasProperty("_YAPS_Bake") && mats[0] != socket.bakedFrom)
                {
                    Undo.RecordObject(socket.renderer, "Remove YAPS socket");
                    mats[0] = socket.bakedFrom;
                    socket.renderer.sharedMaterials = mats;
                    done.Add($"\"{socket.renderer.name}\" back on \"{socket.bakedFrom.name}\"");
                }
            }

            // The objects.
            var go = socket.gameObject;
            if (OwnObject(go, typeof(YapsSocket)))
            {
                Undo.DestroyObjectImmediate(go);
                done.Add($"the object \"{name}\"");
            }
            else
            {
                foreach (var child in OwnChildrenOf(go.transform)) Undo.DestroyObjectImmediate(child.gameObject);
                Undo.DestroyObjectImmediate(socket);
                done.Add($"the component on \"{name}\" and the toolkit's objects under it");
            }

            Undo.CollapseUndoOperations(group);
            return $"Removed socket \"{name}\": " + string.Join(", ", done) + ". Undo brings it all back.";
        }

        // --- plugs -----------------------------------------------------------

        public static string RemovePlug(YapsPlug plug)
        {
            if (plug == null) return null;
            string name = plug.name;
            var avatar = plug.GetComponentInParent<CVRAvatar>();
            var top = TopOf(plug.transform);
            var renderer = plug.Target;
            var done = new List<string>();

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Remove YAPS plug " + name);

            if (renderer != null)
            {
                // The bake: the material it replaced back in its slot; when
                // that cannot be found, the deform off and the source shader
                // back where it is known.
                var slots = BakedSlots(renderer).ToList();
                if (slots.Count > 0)
                {
                    var back = OriginalMaterial(plug, renderer, out string how);
                    var mats = renderer.sharedMaterials;
                    Undo.RecordObject(renderer, "Remove YAPS plug");
                    foreach (int slot in slots)
                    {
                        var m = mats[slot];
                        if (back != null)
                        {
                            mats[slot] = back;
                        }
                        else
                        {
                            Undo.RecordObject(m, "Remove YAPS plug");
                            m.SetFloat("_YAPS_Enabled", 0f);
                            string source = YapsShaderPatcher.SourceShaderOf(m);
                            var shader = source != null ? Shader.Find(source) : null;
                            if (shader != null) m.shader = shader;
                            EditorUtility.SetDirty(m);
                        }
                    }
                    renderer.sharedMaterials = mats;
                    done.Add(back != null ? $"\"{renderer.name}\" back on \"{back.name}\" ({how})" : $"deform off on \"{renderer.name}\" ({how})");
                }

                // The size wiring in the avatar's own clips.
                if (avatar != null)
                {
                    int stripped = StripWiring(avatar, renderer);
                    if (stripped > 0) done.Add($"size wiring out of {stripped} clip(s)");
                }
            }

            if (avatar != null)
            {
                int before = YapsToggles.Edits;
                int entries = RemoveEntries(avatar, ToggleEntriesFor(avatar, plug));
                if (entries > 0) done.Add("its menu toggle");
                string menu = YapsToggles.RefreshMenuAnimator(avatar, before);
                if (menu != null) done.Add(menu.TrimEnd('.'));
            }

            var go = plug.gameObject;
            if (OwnObject(go, typeof(YapsPlug)))
            {
                Undo.DestroyObjectImmediate(go);
                done.Add($"the object \"{name}\"");
            }
            else
            {
                foreach (var child in OwnChildrenOf(go.transform)) Undo.DestroyObjectImmediate(child.gameObject);
                Undo.DestroyObjectImmediate(plug);
                done.Add($"the component on \"{name}\" and its markers");
            }

            Undo.CollapseUndoOperations(group);
            return $"Removed plug \"{name}\": " + string.Join(", ", done) + ". Undo brings it all back.";
        }

        // --- leftovers ---------------------------------------------------------

        // What a socket or plug deleted by hand leaves: layers with no
        // socket, parameters with no layer, toggles aiming at nothing, and
        // the toolkit's objects with no component above them. Returns one
        // line per thing removed; empty when the avatar is clean.
        public static List<string> Sweep(Transform top)
        {
            var done = new List<string>();
            if (top == null) return done;
            var avatar = top.GetComponentInChildren<CVRAvatar>();
            var sockets = top.GetComponentsInChildren<YapsSocket>(true);
            var plugs = top.GetComponentsInChildren<YapsPlug>(true);
            var liveLayers = new HashSet<string>(sockets.Select(YapsSocketReactions.LayerName));

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Clean up YAPS leftovers");

            foreach (var controller in ControllersOf(top))
            {
                // Layers named for a socket that is gone.
                foreach (var layer in controller.layers.ToList())
                {
                    if (!layer.name.StartsWith("YAPS ") || !layer.name.EndsWith(" reactions") || liveLayers.Contains(layer.name)) continue;
                    string socketName = layer.name.Substring(5, layer.name.Length - 5 - 10);
                    string parameter = "#YAPS/" + Sanitise(socketName) + "/Depth";
                    RemoveLayer(new List<AnimatorController> { controller }, layer.name, parameter);
                    done.Add($"layer \"{layer.name}\" in \"{controller.name}\": its socket is gone");
                }
                // Depth parameters no layer reads.
                foreach (var p in controller.parameters.ToList())
                {
                    if (!p.name.StartsWith("#YAPS/") || !p.name.EndsWith("/Depth")) continue;
                    if (ParameterUsed(controller, p.name)) continue;
                    Undo.RegisterCompleteObjectUndo(controller, "Clean up YAPS leftovers");
                    controller.RemoveParameter(p);
                    EditorUtility.SetDirty(controller);
                    done.Add($"parameter \"{p.name}\" in \"{controller.name}\": nothing reads it");
                }
            }

            if (avatar != null && avatar.avatarSettings != null && avatar.avatarSettings.settings != null)
            {
                var dead = new List<ABI.CCK.Scripts.CVRAdvancedSettingsEntry>();
                foreach (var e in avatar.avatarSettings.settings)
                {
                    if (e == null || e.type != ABI.CCK.Scripts.CVRAdvancedSettingsEntry.SettingsType.Toggle || e.toggleSettings == null) continue;
                    var t = e.toggleSettings;
                    if (t.useAnimationClip)
                    {
                        // The toolkit's plug toggle whose plug is gone: its
                        // clip aims at a path with no bake on it any more.
                        if (!Generated(t.animationClip)) continue;
                        string path = AnimationUtility.GetCurveBindings(t.animationClip)
                            .FirstOrDefault(b => b.propertyName == "material._YAPS_Enabled").path;
                        var at = path != null ? avatar.transform.Find(path) : null;
                        var r = at != null ? at.GetComponent<Renderer>() : null;
                        if (r != null && BakedSlots(r).Any()) continue;
                        dead.Add(e);
                        done.Add($"menu toggle \"{e.name}\": its plug is gone");
                    }
                    else if (t.gameObjectTargets != null && t.gameObjectTargets.Count > 0
                             && t.gameObjectTargets.All(g => g == null || g.gameObject == null))
                    {
                        dead.Add(e);
                        done.Add($"menu toggle \"{e.name}\": every object it switched is gone");
                    }
                }
                int before = YapsToggles.Edits;
                RemoveEntries(avatar, dead);
                string menu = YapsToggles.RefreshMenuAnimator(avatar, before);
                if (menu != null) done.Add(menu.TrimEnd('.'));
            }

            // The toolkit's objects with nothing of the toolkit's above them.
            foreach (var t in top.GetComponentsInChildren<Transform>(true).ToList())
            {
                if (t == null || t == top || !OwnChildren.Contains(t.name) || t.parent == null) continue;
                if (t.parent.GetComponent<YapsSocket>() != null || t.parent.GetComponent<YapsPlug>() != null) continue;
                done.Add($"\"{t.name}\" under \"{t.parent.name}\": no socket or plug there any more");
                Undo.DestroyObjectImmediate(t.gameObject);
            }

            Undo.CollapseUndoOperations(group);
            return done;
        }

        // --- pieces ----------------------------------------------------------

        // Every animator controller the avatar plays: the CCK's base, its
        // override's, and the animator's own.
        static List<AnimatorController> ControllersOf(Transform top)
        {
            var list = new List<AnimatorController>();
            void Add(RuntimeAnimatorController c)
            {
                if (c is AnimatorOverrideController o) c = o.runtimeAnimatorController;
                if (c is AnimatorController a && !list.Contains(a)) list.Add(a);
            }
            if (top == null) return list;
            var avatar = top.GetComponentInChildren<CVRAvatar>();
            if (avatar != null && avatar.avatarSettings != null)
            {
                Add(avatar.avatarSettings.baseController);
                Add(avatar.avatarSettings.baseOverrideController);
            }
            foreach (var animator in top.GetComponentsInChildren<Animator>(true)) Add(animator.runtimeAnimatorController);
            return list;
        }

        static int RemoveLayer(List<AnimatorController> controllers, string layerName, string parameter)
        {
            int n = 0;
            foreach (var controller in controllers)
            {
                var layers = controller.layers;
                int index = System.Array.FindIndex(layers, l => l.name == layerName);
                if (index < 0) continue;
                Undo.RegisterCompleteObjectUndo(controller, "Remove YAPS layer");
                // RemoveLayer clears and destroys the embedded machine itself.
                controller.RemoveLayer(index);
                var p = controller.parameters.FirstOrDefault(x => x.name == parameter);
                if (p != null && !ParameterUsed(controller, parameter)) controller.RemoveParameter(p);
                EditorUtility.SetDirty(controller);
                n++;
            }
            if (n > 0) AssetDatabase.SaveAssets();
            return n;
        }

        // Does any layer still read this parameter: a tree blends by it, a
        // transition tests it, or a state's speed or time follows it.
        static bool ParameterUsed(AnimatorController controller, string parameter)
        {
            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (var machine in Machines(layer.stateMachine))
                {
                    foreach (var s in machine.states)
                    {
                        var state = s.state;
                        if (state == null) continue;
                        if (state.speedParameterActive && state.speedParameter == parameter) return true;
                        if (state.timeParameterActive && state.timeParameter == parameter) return true;
                        if (state.mirrorParameterActive && state.mirrorParameter == parameter) return true;
                        if (state.cycleOffsetParameterActive && state.cycleOffsetParameter == parameter) return true;
                        if (state.motion is BlendTree tree && TreeUses(tree, parameter)) return true;
                        foreach (var t in state.transitions)
                            if (t.conditions.Any(c => c.parameter == parameter)) return true;
                    }
                    foreach (var t in machine.anyStateTransitions)
                        if (t.conditions.Any(c => c.parameter == parameter)) return true;
                    foreach (var t in machine.entryTransitions)
                        if (t.conditions.Any(c => c.parameter == parameter)) return true;
                }
            }
            return false;
        }

        static IEnumerable<AnimatorStateMachine> Machines(AnimatorStateMachine root)
        {
            yield return root;
            foreach (var child in root.stateMachines)
                if (child.stateMachine != null)
                    foreach (var m in Machines(child.stateMachine)) yield return m;
        }

        static bool TreeUses(BlendTree tree, string parameter)
        {
            if (tree == null) return false;
            if (tree.blendParameter == parameter || tree.blendParameterY == parameter) return true;
            foreach (var child in tree.children)
            {
                if (child.directBlendParameter == parameter) return true;
                if (child.motion is BlendTree inner && TreeUses(inner, parameter)) return true;
            }
            return false;
        }

        // The toolkit's toggle for an object: a Toggle entry aiming at it and
        // nothing else.
        static IEnumerable<ABI.CCK.Scripts.CVRAdvancedSettingsEntry> ToggleEntriesFor(CVRAvatar avatar, GameObject target)
        {
            if (avatar == null || avatar.avatarSettings == null || avatar.avatarSettings.settings == null) yield break;
            foreach (var e in avatar.avatarSettings.settings)
            {
                if (e == null || e.type != ABI.CCK.Scripts.CVRAdvancedSettingsEntry.SettingsType.Toggle || e.toggleSettings == null) continue;
                var targets = e.toggleSettings.gameObjectTargets;
                if (targets == null || targets.Count == 0 || e.toggleSettings.useAnimationClip) continue;
                if (targets.All(g => g != null && g.gameObject == target)) yield return e;
            }
        }

        // The toolkit's toggle for a plug: an entry with generated clips
        // switching _YAPS_Enabled on the plug's renderer.
        static IEnumerable<ABI.CCK.Scripts.CVRAdvancedSettingsEntry> ToggleEntriesFor(CVRAvatar avatar, YapsPlug plug)
        {
            if (avatar == null || avatar.avatarSettings == null || avatar.avatarSettings.settings == null || plug.Target == null) yield break;
            string path = AnimationUtility.CalculateTransformPath(plug.Target.transform, avatar.transform);
            foreach (var e in avatar.avatarSettings.settings)
            {
                if (e == null || e.type != ABI.CCK.Scripts.CVRAdvancedSettingsEntry.SettingsType.Toggle || e.toggleSettings == null) continue;
                var t = e.toggleSettings;
                if (!t.useAnimationClip || !Generated(t.animationClip)) continue;
                if (AnimationUtility.GetCurveBindings(t.animationClip).Any(b => b.path == path && b.propertyName == "material._YAPS_Enabled"))
                    yield return e;
            }
        }

        static int RemoveEntries(CVRAvatar avatar, IEnumerable<ABI.CCK.Scripts.CVRAdvancedSettingsEntry> entries)
        {
            var list = entries.ToList();
            if (list.Count == 0) return 0;
            Undo.RecordObject(avatar, "Remove YAPS toggle");
            foreach (var e in list) { avatar.avatarSettings.settings.Remove(e); YapsToggles.NoteRemoved(e.machineName); }
            EditorUtility.SetDirty(avatar);
            return list.Count;
        }

        // Slots on a renderer holding a baked YAPS material.
        static IEnumerable<int> BakedSlots(Renderer renderer)
        {
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].HasProperty("_YAPS_Bake") && mats[i].GetTexture("_YAPS_Bake") != null)
                    yield return i;
        }

        // The material a plug's bake replaced: recorded by the bake, or
        // found by name from the clone's, or nothing.
        static Material OriginalMaterial(YapsPlug plug, Renderer renderer, out string how)
        {
            if (plug.bakedFrom != null) { how = "recorded at bake"; return plug.bakedFrom; }
            var mats = renderer.sharedMaterials;
            foreach (int slot in BakedSlots(renderer))
            {
                string name = mats[slot].name;
                if (!name.EndsWith(" (YAPS)")) continue;
                string stem = name.Substring(0, name.Length - 7);
                var found = AssetDatabase.FindAssets("t:Material " + stem)
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => !p.StartsWith(YapsNativeBuilder.OutputRoot + "/"))
                    .Select(AssetDatabase.LoadAssetAtPath<Material>)
                    .Where(m => m != null && m.name == stem)
                    .ToList();
                if (found.Count == 1) { how = "found by name"; return found[0]; }
                if (found.Count > 1)
                {
                    string source = YapsShaderPatcher.SourceShaderOf(mats[slot]);
                    var bySource = found.Where(m => m.shader != null && m.shader.name == source).ToList();
                    if (bySource.Count == 1) { how = "found by name and shader"; return bySource[0]; }
                    how = $"{found.Count} materials named \"{stem}\"; pick the right one by hand";
                    return null;
                }
            }
            how = "the material it replaced was not found";
            return null;
        }

        static IEnumerable<AnimationClip> WiredClips(CVRAvatar avatar, Renderer renderer)
        {
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, avatar.transform);
            foreach (var clip in ControllersOf(avatar.transform).SelectMany(YapsCurveMirror.ClipsOf).Distinct())
            {
                if (Generated(clip)) continue;
                if (AnimationUtility.GetCurveBindings(clip).Any(b => b.path == path && WiredProperties.Contains(b.propertyName)))
                    yield return clip;
            }
        }

        static int StripWiring(CVRAvatar avatar, Renderer renderer)
        {
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, avatar.transform);
            int n = 0;
            foreach (var clip in WiredClips(avatar, renderer).ToList())
            {
                Undo.RegisterCompleteObjectUndo(clip, "Remove YAPS plug");
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    if (b.path == path && WiredProperties.Contains(b.propertyName))
                        AnimationUtility.SetEditorCurve(clip, b, null);
                EditorUtility.SetDirty(clip);
                n++;
            }
            if (n > 0) AssetDatabase.SaveAssets();
            return n;
        }

        // An object the toolkit made outright: nothing on it but its
        // transform and the YAPS component, and nothing under it but the
        // toolkit's own. A bone or a mesh with a component added is not.
        static bool OwnObject(GameObject go, System.Type component)
        {
            // The test and preview plugs carry a mesh of the toolkit's own.
            if (go.name == "YAPS Test Plug" || go.name == YapsPreview.PlugName) return true;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c is Transform || c == null) continue;
                if (c.GetType() != component) return false;
            }
            for (int i = 0; i < go.transform.childCount; i++)
                if (!OwnChildren.Contains(go.transform.GetChild(i).name)) return false;
            return true;
        }

        static IEnumerable<Transform> OwnChildrenOf(Transform t)
        {
            for (int i = 0; i < t.childCount; i++)
                if (OwnChildren.Contains(t.GetChild(i).name)) yield return t.GetChild(i);
        }

        static Transform TopOf(Transform t)
        {
            var avatar = t.GetComponentInParent<CVRAvatar>();
            return avatar != null ? avatar.transform : t.root;
        }

        static bool Generated(AnimationClip clip)
        {
            string path = clip != null ? AssetDatabase.GetAssetPath(clip) : null;
            return !string.IsNullOrEmpty(path)
                   && path.Replace('\\', '/').StartsWith(YapsNativeBuilder.OutputRoot + "/", System.StringComparison.OrdinalIgnoreCase);
        }

        static string Sanitise(string s)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
#endif
