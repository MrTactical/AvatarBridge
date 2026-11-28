#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge
{
    // The Toolkit's "VRChat accessories" card: accessories dropped onto an avatar already on
    // ChilloutVR, converted in place. They convert on a stand-in, a copy of the avatar with a
    // VRChat descriptor, the way they would on a VRChat avatar, and only what the conversion made
    // for them comes back. Here, not in the Toolkit, since the add-on package ships without the
    // converter.
    [InitializeOnLoad]
    static class AccessoryAttach
    {
        const string Category = "Accessories";
        const string UndoName = "Convert accessories";

        static AccessoryAttach()
        {
            ToolkitPanel.AttachAccessories = Run;
            // Detach takes the newest press still on; Re-attach the newest press, on or off.
            ToolkitPanel.DetachAccessories = (target, folder) => AccessoryRecord.Detach(target, AccessoryRecord.FindIn(folder).FirstOrDefault(r => !r.detached));
            ToolkitPanel.ReattachAccessories = (target, settings, write, folder) =>
                AccessoryRecord.Reattach(target, AccessoryRecord.FindIn(folder).FirstOrDefault(), settings, write);
        }

        // An object under the avatar carrying VRChat, VRCFury or Modular Avatar components, at the
        // root of the prefab it came in as. A converted avatar keeps none of these but the
        // PhysBones "Delete PhysBones after converting" kept, so a PhysBone counts only when no
        // converted chain moves its bones and it came in a prefab of its own.
        internal static List<GameObject> Find(GameObject target) => Find(target, out _);

        static List<GameObject> Find(GameObject target, out int ownPhysBones)
        {
            var found = new List<GameObject>();
            var own = new List<Transform>();
            ownPhysBones = 0;
            if (target == null) return found;
            HashSet<Transform> simulated = null;
            var hips = target.GetComponent<Animator>() is Animator anim && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            foreach (var c in target.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.transform == target.transform) continue;
                string ns = c.GetType().Namespace ?? "", type = c.GetType().Name;
                bool fury = ns.StartsWith("VF.") || ns.StartsWith("nadena.dev.modular_avatar");
                if (!fury && !ns.StartsWith("VRC.")) continue;
                // A collider alone never makes an accessory: the PhysBone using it does.
                if (type.Contains("PhysBoneCollider")) continue;
                bool physBone = !fury && type.Contains("PhysBone");
                if (physBone)
                {
                    if (simulated == null) simulated = SimulatedBones(target);
                    if (Simulated(c, simulated)) continue;
                }
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(c.gameObject);
                bool loose = root == null || root == target || !root.transform.IsChildOf(target.transform);
                // Unconverted, on the avatar itself or inside its own model: the PhysBones card's job.
                if (physBone && (loose || PrefabUtility.GetPrefabAssetType(root) == PrefabAssetType.Model))
                {
                    own.Add(c.transform);
                    continue;
                }
                var at = loose ? c.gameObject : Top(root, target, hips);
                if (found.Any(f => at.transform.IsChildOf(f.transform))) continue;
                found.RemoveAll(f => f.transform.IsChildOf(at.transform));
                found.Add(at);
            }
            ownPhysBones = own.Count(t => !found.Any(f => t.IsChildOf(f.transform)));
            return found;
        }

        // The highest prefab dropped around this one, since a prefab nested inside an accessory
        // cannot be replaced alone. Never one holding the avatar's own skeleton.
        static GameObject Top(GameObject root, GameObject target, Transform hips)
        {
            var best = root;
            for (var t = root.transform.parent; t != null && t != target.transform; t = t.parent)
                if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && CanReplace(t.gameObject) && (hips == null || !hips.IsChildOf(t)))
                    best = t.gameObject;
            return best;
        }

        // Bones a converted cloth or DynamicBone already moves.
        static HashSet<Transform> SimulatedBones(GameObject target)
        {
            var bones = new HashSet<Transform>();
            foreach (var c in target.GetComponentsInChildren<Component>(true))
                if (c != null && (c.GetType().Name == "MagicaCloth" || c.GetType().Name == "DynamicBone"))
                    bones.UnionWith(Refs(c));
            return bones;
        }

        static bool Simulated(Component physBone, HashSet<Transform> bones)
        {
            var root = new SerializedObject(physBone).FindProperty("rootTransform")?.objectReferenceValue as Transform;
            if (root == null) root = physBone.transform;
            return bones.Contains(root) || root.Cast<Transform>().Any(bones.Contains);
        }

        internal static BridgeReport Run(GameObject target, BridgeSettings settings, System.Func<BridgeReport, string> folder)
        {
            var report = new BridgeReport();
            if (target == null || target.GetComponent<CVRAvatar>() == null)
            {
                report.Warning(Category, "No CVRAvatar on its root", "This card is for an avatar already on ChilloutVR.");
                return report;
            }
            if (EditorUtility.IsPersistent(target))
            {
                report.Warning(Category, "This is a prefab asset", "Drag the avatar in from the scene instead.");
                return report;
            }
            if (target.GetComponent<VRCAvatarDescriptor>() != null)
            {
                report.Warning(Category, "It also carries a VRChat avatar descriptor",
                    "Convert it in the Convert tab instead, with the accessories on it: that is the normal route.");
                return report;
            }
            var accessories = Find(target, out int ownPhysBones);
            if (ownPhysBones > 0)
                report.Warning(Category, $"{BridgeElements.Count(ownPhysBones, "PhysBone", "PhysBones")} on the avatar itself left alone",
                    "They did not come in an accessory of their own, so they read as the avatar's. The PhysBones to cloth card converts those.");
            // An object saved inside the avatar's prefab cannot be deleted from the scene, so it
            // cannot be swapped for its conversion.
            foreach (var a in accessories.Where(a => !CanReplace(a)).ToList())
            {
                report.Warning(Category, $"\"{a.name}\" is part of the avatar's prefab",
                    "It cannot be replaced while the prefab holds it. Unpack the avatar (right-click it, Prefab, Unpack) and press again.");
                accessories.Remove(a);
            }
            if (accessories.Count == 0) return report;

            // Its own folder each time: the stand-in is unsaved, so the converter would share a folder by name.
            string home = folder(report);
            string name = "Accessories";
            for (int n = 2; AssetDatabase.IsValidFolder(home + "/" + name); n++) name = "Accessories " + n;
            string dir = home + "/" + name;
            AssetDatabase.CreateFolder(home, name);

            var roots = new HashSet<GameObject>(LoadedRoots());
            var standIn = Object.Instantiate(target, target.transform.parent);
            standIn.name = name;
            standIn.SetActive(true);
            GameObject output = null;
            bool converted = false;
            try
            {
                var descriptor = standIn.AddComponent<VRCAvatarDescriptor>();
                descriptor.ViewPosition = target.GetComponent<CVRAvatar>().viewPosition;
                // The SDK's inspector fills these on first draw; added from a script they stay null,
                // and VRCFury's and the converter's controller walks both throw.
                VRCAvatarDescriptor.CustomAnimLayer Layer(VRCAvatarDescriptor.AnimLayerType t) => new VRCAvatarDescriptor.CustomAnimLayer { type = t, isDefault = true };
                descriptor.baseAnimationLayers = new[] { VRCAvatarDescriptor.AnimLayerType.Base, VRCAvatarDescriptor.AnimLayerType.Additive,
                    VRCAvatarDescriptor.AnimLayerType.Gesture, VRCAvatarDescriptor.AnimLayerType.Action, VRCAvatarDescriptor.AnimLayerType.FX }.Select(Layer).ToArray();
                descriptor.specialAnimationLayers = new[] { VRCAvatarDescriptor.AnimLayerType.Sitting, VRCAvatarDescriptor.AnimLayerType.TPose,
                    VRCAvatarDescriptor.AnimLayerType.IKPose }.Select(Layer).ToArray();
                // An empty FX of its own: a default one is the SDK's face layer, which no accessory brought.
                descriptor.customizeAnimationLayers = true;
                descriptor.baseAnimationLayers[4].isDefault = false;
                descriptor.baseAnimationLayers[4].animatorController = AnimatorController.CreateAnimatorControllerAtPath(dir + "/Empty FX.controller");

                var s = JsonUtility.FromJson<BridgeSettings>(JsonUtility.ToJson(settings));
                s.outputFolder = home;
                // Never resize the project's textures from here: the avatar's were sized when it converted.
                s.slimTexturesOnConvert = false;
                // Kept PhysBones would come back on the copies and read as accessories next time.
                s.deleteConvertedPhysBones = true;
                var result = BridgeConverter.Convert(descriptor, s);
                output = result.ConvertedRoot;
                // The avatar's own diagnostics are the Check before upload card's: here they would
                // read as the accessories' fault.
                bool Bake(ReportEntry e) => e.Category == "VRCFury" || e.Category == "Modular Avatar";
                foreach (var e in result.Entries.Where(e => e.Status == ReportStatus.Error && e.Category != "Diagnostics"
                    || e.Status == ReportStatus.Warning && Bake(e)))
                    report.Add(e.Status, e.Category, e.Subject, e.Detail);
                // Without its bake an accessory comes out stripped of its VRCFury or Modular Avatar
                // features, and swapping that in would lose them.
                bool unbaked = result.Entries.Any(e => Bake(e) && (e.Status == ReportStatus.Error || e.Subject.StartsWith("Converting WITHOUT")));
                if (output == null || unbaked)
                {
                    report.Error(Category, "The accessories did not convert", "Nothing on the avatar was changed. The errors above say why.");
                    return report;
                }
                converted = true;
                Carry(report, target, output, accessories, dir);
            }
            finally
            {
                // Made outside the Undo record, so none of it comes back with Ctrl+Z. A conversion
                // that threw leaves its working copy, a new root named after the stand-in.
                var made = LoadedRoots().Where(g => !roots.Contains(g) && g.name.StartsWith(name)).ToList();
                made.Add(output);
                made.Add(standIn);
                foreach (var g in made.Where(g => g != null))
                {
                    Undo.ClearUndo(g);
                    Object.DestroyImmediate(g);
                }
                AssetDatabase.DeleteAsset(dir + "/Empty FX.controller");
                if (!converted) AssetDatabase.DeleteAsset(dir);
            }
            return report;
        }

        static List<GameObject> LoadedRoots() => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .Where(s => s.isLoaded).SelectMany(s => s.GetRootGameObjects()).ToList();

        static bool CanReplace(GameObject a) => !PrefabUtility.IsPartOfPrefabInstance(a)
            || PrefabUtility.GetOutermostPrefabInstanceRoot(a) == a || PrefabUtility.IsAddedGameObjectOverride(a);

        static void Carry(BridgeReport report, GameObject target, GameObject output, List<GameObject> accessories, string dir)
        {
            var into = target.transform;
            var from = output.transform;
            var intoAt = Index(into);
            var fromAt = Index(from);
            var intoKey = intoAt.ToDictionary(p => p.Value, p => p.Key);
            var fromKey = fromAt.ToDictionary(p => p.Value, p => p.Key);

            // Where Armature Link or a bone proxy moved a whole accessory: new on the copy, hung on
            // something both share. VRCFury wraps it as "[VF123] name/Original Object".
            var newTops = from.GetComponentsInChildren<Transform>(true).Where(t => t != from && !intoAt.ContainsKey(fromKey[t])
                && (t.parent == from || intoAt.ContainsKey(fromKey[t.parent]))).ToList();
            Transform MovedTo(GameObject a)
            {
                // "from <root>" when its root is a bone of a mesh; animated parents add "Toggle From" holders.
                var wrapped = new Regex(@"^\[VF\d+\] " + Regex.Escape(a.name) + "( from .+)?$");
                var hits = newTops.Where(t => wrapped.IsMatch(t.name) && Original(t) != null).ToList();
                if (hits.Count == 0) hits = newTops.Where(t => t.name == a.name).ToList();
                return hits.Count == 1 ? hits[0] : null;
            }

            // What the conversion made of each accessory. One it removed stays as it was:
            // replacing it would lose it.
            var made = new Dictionary<GameObject, Transform>();
            var consumed = new List<GameObject>();
            foreach (var a in accessories)
                if (fromAt.TryGetValue(intoKey[a.transform], out var m) || (m = MovedTo(a)) != null) made[a] = m;
                // Logic alone, a menu or a controller: the bake used it up, and it goes if its toggles come.
                else if (a.GetComponentsInChildren<Renderer>(true).Length == 0) consumed.Add(a);
                else report.Warning(Category, $"\"{a.name}\" was not converted",
                    "The conversion moved or removed it, so it stays on the avatar as it was.");
            if (made.Count == 0 && consumed.Count == 0)
            {
                KeepUsed(dir, new Transform[0], new string[0]);
                return;
            }
            var carried = new HashSet<Transform>();
            foreach (var m in made.Values) carried.UnionWith(m.GetComponentsInChildren<Transform>(true));

            // And what it made for them elsewhere: Armature Link's bone holders, the cloth for their
            // PhysBones. New on the copy, and tied to the accessories by a reference either way;
            // what it made for the avatar itself stays behind.
            var added = from.GetComponentsInChildren<Transform>(true)
                .Where(t => t != from && !carried.Contains(t) && !intoAt.ContainsKey(fromKey[t])).ToList();
            var addedSet = new HashSet<Transform>(added);
            var groups = added.Where(t => !addedSet.Contains(t.parent)).Select(t => t.GetComponentsInChildren<Transform>(true).ToList()).ToList();
            var refs = new Dictionary<Transform, HashSet<Transform>>();
            HashSet<Transform> RefsOf(Transform t) => refs.TryGetValue(t, out var r) ? r : refs[t] = SceneRefs(t);
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var g in groups.Where(g => !carried.Contains(g[0])).ToList())
                {
                    var members = new HashSet<Transform>(g);
                    if (g.Any(t => RefsOf(t).Overlaps(carried)) || carried.Any(t => RefsOf(t).Overlaps(members)))
                    {
                        carried.UnionWith(g);
                        grew = true;
                    }
                }
            }

            var names = new List<string>();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            var copyOf = new Dictionary<Transform, Transform>();
            var tops = new List<GameObject>();
            var written = new List<string>();
            int lost = 0;
            try
            {
                foreach (var top in carried.Where(t => !carried.Contains(t.parent)).OrderBy(t => fromKey[t]))
                {
                    var parent = top.parent == from ? into : intoAt.TryGetValue(fromKey[top.parent], out var p) ? p : null;
                    if (parent == null)
                    {
                        report.Warning(Category, $"\"{top.name}\" had nowhere to go",
                            $"Its parent \"{AnimationUtility.CalculateTransformPath(top.parent, from)}\" is not on the avatar, so it was left out.");
                        continue;
                    }
                    var copy = Object.Instantiate(top.gameObject, parent, false);
                    copy.name = top.name;
                    // Where the original sits, so the Hierarchy keeps its order.
                    if (intoAt.TryGetValue(fromKey[top], out var was) && was.parent == parent) copy.transform.SetSiblingIndex(was.GetSiblingIndex());
                    Undo.RegisterCreatedObjectUndo(copy, UndoName);
                    tops.Add(copy);
                    var a = top.GetComponentsInChildren<Transform>(true);
                    var b = copy.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < a.Length; i++) copyOf[a[i]] = b[i];
                }
                // Where something on the conversion lands on the avatar: its copy, else the avatar's
                // object at the same place. Null when it has neither.
                Object Remap(Object value)
                {
                    var at = TransformOf(value);
                    var to = copyOf.TryGetValue(at, out var mine) ? mine : at == from ? into : intoAt.TryGetValue(fromKey[at], out var theirs) ? theirs : null;
                    return to == null ? null : Same(value, at, to);
                }
                // Each copy's references out of its own piece: onto another piece's copy, else onto
                // the avatar's object at the same place.
                foreach (var t in copyOf.Values)
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c == null || c is Transform) continue;
                        var so = new SerializedObject(c);
                        var it = so.GetIterator();
                        while (it.Next(true))
                        {
                            if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                            var at = TransformOf(it.objectReferenceValue);
                            if (at == null || !at.IsChildOf(from)) continue;
                            var value = Remap(it.objectReferenceValue);
                            if (value == null) lost++;
                            it.objectReferenceValue = value;
                        }
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }

                var carriedPaths = new HashSet<string>(copyOf.Values.Select(t => AnimationUtility.CalculateTransformPath(t, into)));
                written = CarryLogic(report, target.GetComponent<CVRAvatar>(), output, dir, carriedPaths, Remap, ref lost,
                    out var wiredSwaps, out var addedEntries);
                // Nothing else shows a logic-only accessory converted. ponytail: one press's toggles
                // vouch for every such accessory in it; trace each layer to its source controller if
                // mixed presses lose one.
                if (wiredSwaps == null && addedEntries.Count == 0)
                {
                    foreach (var a in consumed)
                        report.Warning(Category, $"\"{a.name}\" was not converted",
                            "Nothing the conversion made for it could be carried, so it stays on the avatar as it was.");
                    consumed.Clear();
                }
                if (made.Count == 0 && consumed.Count == 0)
                {
                    KeepUsed(dir, new Transform[0], new string[0]);
                    return;
                }
                names = made.Keys.Concat(consumed).Select(a => a.name).ToList();

                // The avatar's own references into what is replaced, onto the copies: a mesh skinned
                // to an accessory's bones, a constraint aimed at one. Recorded, so Ctrl+Z restores them.
                var replaced = new HashSet<Transform>(made.Keys.Concat(consumed).SelectMany(a => a.GetComponentsInChildren<Transform>(true)));
                // A moved accessory's parts, found under its new place by their place inside it.
                var movedAt = made.Where(p => !fromAt.ContainsKey(intoKey[p.Key.transform]))
                    .ToDictionary(p => p.Key.transform, p => Index(Original(p.Value) ?? p.Value));
                Transform OutputOf(Transform o)
                {
                    if (fromAt.TryGetValue(intoKey[o], out var m)) return m;
                    foreach (var p in movedAt)
                        if (o.IsChildOf(p.Key) && p.Value.TryGetValue(KeyOf(p.Key, o), out m)) return m;
                    return null;
                }
                var swap = new Dictionary<Transform, Transform>();
                foreach (var o in replaced)
                    if (OutputOf(o) is Transform m && copyOf.TryGetValue(m, out var cp)) swap[o] = cp;
                var copies = new HashSet<Transform>(copyOf.Values);
                foreach (var c in into.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || replaced.Contains(c.transform) || copies.Contains(c.transform)) continue;
                    var so = new SerializedObject(c);
                    var it = so.GetIterator();
                    bool changed = false;
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                        var at = TransformOf(it.objectReferenceValue);
                        if (at == null || !replaced.Contains(at)) continue;
                        var value = swap.TryGetValue(at, out var to) ? Same(it.objectReferenceValue, at, to) : null;
                        if (value == null) lost++;
                        it.objectReferenceValue = value;
                        changed = true;
                    }
                    if (changed) so.ApplyModifiedProperties();
                }

                // Read before the originals go: the record re-attaches them from their prefabs.
                var dropped = made.Keys.Concat(consumed).Select(a => AccessoryRecord.Capture(a, target)).ToList();
                foreach (var a in made.Keys.Concat(consumed).ToList()) Undo.DestroyObjectImmediate(a);
                written.Add(AccessoryRecord.Save(dir, target, dropped, tops, wiredSwaps, addedEntries));
                if (lost > 0)
                    report.Warning(Category, $"{BridgeElements.Count(lost, "reference", "references")} had no match on the avatar",
                        "Each pointed at something the conversion made or removed that the avatar has no match for, and now points at nothing.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            bool kept = KeepUsed(dir, copyOf.Values, written);
            var cloth = copyOf.Values.Count(t => t.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "MagicaCloth"));
            report.Converted(Category, $"Converted {BridgeElements.Count(names.Count, "accessory", "accessories")} in place",
                $"{AvatarSurvey.Few(names)}. {BridgeElements.Count(copyOf.Count, "object", "objects")} came back" +
                (cloth > 0 ? $", {cloth} of them cloth" : "") + "." + (kept ? $" Files made for them are in \"{dir}\"." : "") + " Ctrl+Z undoes it.");
        }

        // The accessories' toggles: the layers the conversion made for them, the parameters and
        // menu entries those use, into copies of the avatar's controllers in their folder. Never
        // its own controller, which other avatars may share. Returns the files written.
        static List<string> CarryLogic(BridgeReport report, CVRAvatar avatar, GameObject output, string dir,
            HashSet<string> carriedPaths, System.Func<Object, Object> remap, ref int lost,
            out Dictionary<Object, Object> wiredSwaps, out List<string> addedEntries)
        {
            wiredSwaps = null;
            addedEntries = new List<string>();
            var written = new List<string>();
            var source = output.GetComponent<CVRAvatar>();
            var theirs = Shipped(source, output.GetComponent<Animator>(), out _);
            var ours = Shipped(avatar, avatar.GetComponent<Animator>(), out var over);
            if (theirs == null) return written;
            var have = new HashSet<string>(ours != null ? ours.layers.Select(l => l.name) : new string[0]);
            // VRCFury's layers on the stand-in are all the accessories': the avatar's own were baked
            // away when it converted. Any other layer counts when it animates what came back; one the
            // avatar already has by name is the converter's own, made again.
            var layers = theirs.layers.Where(l => l.syncedLayerIndex < 0 && l.stateMachine != null
                && (Fury(l.name) || !have.Contains(l.name) && Touches(l.stateMachine, carriedPaths))).ToList();
            var used = new HashSet<string>(layers.SelectMany(l => Parameters(l.stateMachine)));
            var had = new HashSet<string>(avatar.avatarSettings?.settings?.Select(e => e.machineName) ?? new string[0]);
            var entries = source.avatarSettings?.settings?.Where(e => !had.Contains(e.machineName)
                && (used.Contains(e.machineName) || used.Contains(e.machineName + "-x"))).ToList() ?? new List<CVRAdvancedSettingsEntry>();
            if (layers.Count == 0 && entries.Count == 0) return written;
            if (ours == null)
            {
                report.Warning(Category, "Their toggles stayed behind", "The avatar has no animator controller to carry them into.");
                return written;
            }

            string stem = dir + "/" + CvrSetup.SafeFolderName(avatar.name);
            string path = AssetDatabase.GenerateUniqueAssetPath(stem + ".controller");
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(ours), path))
            {
                report.Warning(Category, "Their toggles stayed behind", $"\"{AssetDatabase.GetAssetPath(ours)}\" could not be copied.");
                return written;
            }
            written.Add(path);
            var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            var swaps = new Dictionary<Object, Object> { [ours] = copy };
            if (over != null)
            {
                string overPath = AssetDatabase.GenerateUniqueAssetPath(stem + ".overrideController");
                var overCopy = AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(over), overPath)
                    ? AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overPath) : null;
                if (overCopy != null) written.Add(overPath);
                if (overCopy != null)
                {
                    overCopy.runtimeAnimatorController = copy;
                    EditorUtility.SetDirty(overCopy);
                }
                // Measured, as the PhysBones card does: a swap keyed on a clip the copy replaced is gone.
                if (overCopy == null || PhysBonesInPlace.Live(overCopy) != PhysBonesInPlace.Live(over))
                {
                    report.Warning(Category, "Their toggles stayed behind", "The avatar's override controller swaps clips that a copy of its controller would lose.");
                    // Unwired, so nothing should keep them.
                    foreach (var w in written) AssetDatabase.DeleteAsset(w);
                    written.Clear();
                    return written;
                }
                swaps[over] = overCopy;
            }

            var types = copy.parameters.GroupBy(p => p.name).ToDictionary(g => g.Key, g => g.First().type);
            var clashes = new List<string>();
            int parameters = 0;
            foreach (var p in theirs.parameters.Where(p => used.Contains(p.name)))
            {
                if (types.TryGetValue(p.name, out var type))
                {
                    if (type != p.type) clashes.Add($"{p.name} ({type} on the avatar, {p.type} on the accessories)");
                    continue;
                }
                copy.AddParameter(AnimatorDeepCopier.CloneParameter(p));
                types[p.name] = p.type;
                parameters++;
            }
            var copier = new AnimatorDeepCopier();
            var all = copy.layers.ToList();
            var names = new HashSet<string>(all.Select(l => l.name));
            foreach (var layer in layers)
            {
                var clone = copier.CloneLayer(layer);
                for (int n = 2; names.Contains(clone.name); n++) clone.name = layer.name + " (accessories" + (n > 2 ? " " + (n - 1) : "") + ")";
                names.Add(clone.name);
                all.Add(clone);
                copy.layers = all.ToArray();
                AnimatorAssetSaver.EmbedLayer(clone, copy);
            }
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssets();
            PhysBonesInPlace.Wire(avatar.gameObject, swaps);
            wiredSwaps = swaps;

            // The menu entries, with what they point at on the conversion moved onto the avatar.
            Undo.RecordObject(avatar, UndoName);
            if (avatar.avatarSettings == null)
                avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
            if (avatar.avatarSettings.settings == null) avatar.avatarSettings.settings = new List<CVRAdvancedSettingsEntry>();
            avatar.avatarUsesAdvancedSettings = true;
            avatar.avatarSettings.settings.AddRange(entries);
            addedEntries = entries.Select(e => e.machineName).ToList();
            var so = new SerializedObject(avatar);
            so.Update();
            var it = so.GetIterator();
            var from = output.transform;
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                var at = TransformOf(it.objectReferenceValue);
                if (at == null || !at.IsChildOf(from)) continue;
                var value = remap(it.objectReferenceValue);
                if (value == null) lost++;
                it.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(avatar);
            PrefabUtility.RecordPrefabInstancePropertyModifications(avatar);

            report.Converted(Category, $"Carried their toggles: {BridgeElements.Count(layers.Count, "layer", "layers")}, " +
                $"{BridgeElements.Count(parameters, "parameter", "parameters")} and {BridgeElements.Count(entries.Count, "menu entry", "menu entries")}",
                (entries.Count > 0 ? AvatarSurvey.Few(entries.Select(e => e.name).ToList()) + ". " : "") +
                $"Into a copy of the avatar's controller, \"{path}\", now wired in its place; its own controller is not edited.");
            if (clashes.Count > 0)
                report.Warning(Category, $"{BridgeElements.Count(clashes.Count, "parameter has", "parameters have")} another type on the avatar",
                    string.Join(", ", clashes) + ". The avatar's type was kept, so the accessories' toggles that read it may misbehave.");
            int bits = avatar.GetParameterSyncUsage().Item2;
            if (bits > 3200)
                report.Warning(Category, $"{bits} of 3200 sync bits used, over the limit",
                    "With the accessories' menu entries the avatar syncs more than ChilloutVR allows. Remove entries, or make some local.");
            return written;
        }

        static bool Fury(string layer) => layer.Contains("[VF") || layer.Contains("VRCFury");

        // The controller ChilloutVR ships: the override controller's base when there is one.
        static AnimatorController Shipped(CVRAvatar avatar, Animator animator, out AnimatorOverrideController over)
        {
            over = avatar != null && avatar.overrides != null ? avatar.overrides
                : animator != null ? animator.runtimeAnimatorController as AnimatorOverrideController : null;
            return over != null ? over.runtimeAnimatorController as AnimatorController
                : animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
        }

        static IEnumerable<AnimatorState> States(AnimatorStateMachine m) =>
            m.states.Select(s => s.state).Concat(m.stateMachines.SelectMany(c => States(c.stateMachine)));

        static IEnumerable<AnimatorTransitionBase> Transitions(AnimatorStateMachine m) =>
            m.anyStateTransitions.Cast<AnimatorTransitionBase>().Concat(m.entryTransitions)
                .Concat(m.states.SelectMany(s => s.state.transitions))
                .Concat(m.stateMachines.SelectMany(c => m.GetStateMachineTransitions(c.stateMachine).Concat(Transitions(c.stateMachine))));

        static IEnumerable<Motion> Motions(Motion motion) =>
            motion == null ? Enumerable.Empty<Motion>()
            : motion is BlendTree tree ? new[] { motion }.Concat(tree.children.SelectMany(c => Motions(c.motion)))
            : new[] { motion };

        static bool Touches(AnimatorStateMachine machine, HashSet<string> paths) =>
            States(machine).SelectMany(s => Motions(s.motion)).OfType<AnimationClip>().Distinct()
                .Any(clip => AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    .Any(b => paths.Contains(b.path)));

        static IEnumerable<string> Parameters(AnimatorStateMachine machine)
        {
            foreach (var t in Transitions(machine))
                foreach (var c in t.conditions) yield return c.parameter;
            foreach (var s in States(machine))
            {
                if (s.speedParameterActive) yield return s.speedParameter;
                if (s.timeParameterActive) yield return s.timeParameter;
                if (s.cycleOffsetParameterActive) yield return s.cycleOffsetParameter;
                if (s.mirrorParameterActive) yield return s.mirrorParameter;
                foreach (var tree in Motions(s.motion).OfType<BlendTree>())
                {
                    yield return tree.blendParameter;
                    if (tree.blendType != BlendTreeType.Simple1D && tree.blendType != BlendTreeType.Direct) yield return tree.blendParameterY;
                    if (tree.blendType == BlendTreeType.Direct)
                        foreach (var c in tree.children) yield return c.directBlendParameter;
                }
            }
        }

        // VRCFury's moved object under its Armature Link wrapper, below any "Toggle From" holders.
        static Transform Original(Transform wrapper) =>
            wrapper.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == "Original Object");

        // Every object by a key that survives slashes in names and same-named siblings: each name
        // with its place among the siblings that share it.
        static Dictionary<string, Transform> Index(Transform root) =>
            root.GetComponentsInChildren<Transform>(true).ToDictionary(t => KeyOf(root, t));

        static string KeyOf(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (; t != null && t != root; t = t.parent)
            {
                int n = 0;
                for (int i = 0, at = t.GetSiblingIndex(); i < at; i++)
                    if (t.parent.GetChild(i).name == t.name) n++;
                parts.Add(t.name + "\u0001" + n);
            }
            parts.Reverse();
            return string.Join("\u0000", parts);
        }

        static Transform TransformOf(Object o) => o is GameObject g ? g.transform : o is Component c ? c.transform : null;

        // The same kind of thing on `to`: the object itself, or its n-th component of that type.
        static Object Same(Object value, Transform at, Transform to)
        {
            if (value is GameObject) return to.gameObject;
            if (value is Transform) return to;
            var type = value.GetType();
            int n = System.Array.IndexOf(at.GetComponents(type), value);
            var theirs = to.GetComponents(type);
            return n >= 0 && n < theirs.Length ? theirs[n] : null;
        }

        // The copy's own prefab, controllers and reports are of no use here: only the files the
        // carried objects use stay, with what those use in turn. False when none are left.
        static bool KeepUsed(string dir, IEnumerable<Transform> copies, IEnumerable<string> written)
        {
            var used = new HashSet<string>(written);
            foreach (var t in copies)
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    var it = new SerializedObject(c).GetIterator();
                    while (it.Next(true))
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null)
                        {
                            string path = AssetDatabase.GetAssetPath(it.objectReferenceValue);
                            if (path.StartsWith(dir + "/")) used.Add(path);
                        }
                }
            var keep = new HashSet<string>(AssetDatabase.GetDependencies(used.ToArray(), true));
            var all = AssetDatabase.FindAssets("", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
            foreach (var path in all.Where(p => !AssetDatabase.IsValidFolder(p) && !keep.Contains(p)))
                AssetDatabase.DeleteAsset(path);
            foreach (var folder in all.Where(AssetDatabase.IsValidFolder).Append(dir).OrderByDescending(f => f.Length))
                if (AssetDatabase.FindAssets("", new[] { folder }).Length == 0) AssetDatabase.DeleteAsset(folder);
            return AssetDatabase.IsValidFolder(dir);
        }

        static IEnumerable<Transform> Refs(Component c)
        {
            var it = new SerializedObject(c).GetIterator();
            while (it.Next(true))
                if (it.propertyType == SerializedPropertyType.ObjectReference && TransformOf(it.objectReferenceValue) is Transform r)
                    yield return r;
        }

        static HashSet<Transform> SceneRefs(Transform t)
        {
            var found = new HashSet<Transform>();
            foreach (var c in t.GetComponents<Component>())
                if (c != null && !(c is Transform))
                    found.UnionWith(Refs(c).Where(r => r != t));
            return found;
        }
    }
}
#endif
