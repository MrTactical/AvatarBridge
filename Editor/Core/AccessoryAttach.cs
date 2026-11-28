#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
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

        static AccessoryAttach() => ToolkitPanel.AttachAccessories = Run;

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
                var at = loose ? c.gameObject : root;
                if (found.Any(f => at.transform.IsChildOf(f.transform))) continue;
                found.RemoveAll(f => f.transform.IsChildOf(at.transform));
                found.Add(at);
            }
            ownPhysBones = own.Count(t => !found.Any(f => t.IsChildOf(f.transform)));
            return found;
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
                foreach (var e in result.Entries.Where(e => e.Status == ReportStatus.Error
                    || e.Status == ReportStatus.Warning && (e.Category == "VRCFury" || e.Category == "Modular Avatar")))
                    report.Add(e.Status, e.Category, e.Subject, e.Detail);
                if (output == null)
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

            // What the conversion made of each accessory, where it was dropped. One it moved or
            // removed stays as it was: replacing it would lose it.
            var made = new Dictionary<GameObject, Transform>();
            foreach (var a in accessories)
                if (fromAt.TryGetValue(intoKey[a.transform], out var m)) made[a] = m;
                else report.Warning(Category, $"\"{a.name}\" was not converted",
                    "The conversion moved or removed it, so it stays on the avatar as it was.");
            if (made.Count == 0)
            {
                KeepUsed(dir, new Transform[0]);
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

            var names = made.Keys.Select(a => a.name).ToList();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            var copyOf = new Dictionary<Transform, Transform>();
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
                    var a = top.GetComponentsInChildren<Transform>(true);
                    var b = copy.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < a.Length; i++) copyOf[a[i]] = b[i];
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
                            var to = copyOf.TryGetValue(at, out var mine) ? mine : at == from ? into : intoAt.TryGetValue(fromKey[at], out var theirs) ? theirs : null;
                            var value = to == null ? null : Same(it.objectReferenceValue, at, to);
                            if (value == null) lost++;
                            it.objectReferenceValue = value;
                        }
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }

                // The avatar's own references into what is replaced, onto the copies: a mesh skinned
                // to an accessory's bones, a constraint aimed at one. Recorded, so Ctrl+Z restores them.
                var replaced = new HashSet<Transform>(made.Keys.SelectMany(a => a.GetComponentsInChildren<Transform>(true)));
                var swap = new Dictionary<Transform, Transform>();
                foreach (var o in replaced)
                    if (fromAt.TryGetValue(intoKey[o], out var m) && copyOf.TryGetValue(m, out var cp)) swap[o] = cp;
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

                foreach (var a in made.Keys) Undo.DestroyObjectImmediate(a);
                if (lost > 0)
                    report.Warning(Category, $"{BridgeElements.Count(lost, "reference", "references")} had no match on the avatar",
                        "Each pointed at something the conversion made or removed that the avatar has no match for, and now points at nothing.");
            }
            finally
            {
                Undo.CollapseUndoOperations(undoGroup);
            }

            // The logic comes next: until then, say exactly what stayed behind. Entries the avatar
            // already has came from the converter's own options, not the accessories.
            var had = new HashSet<string>(target.GetComponent<CVRAvatar>().avatarSettings?.settings?.Select(e => e.machineName) ?? new string[0]);
            var menu = output.GetComponent<CVRAvatar>().avatarSettings?.settings?.Where(e => !had.Contains(e.machineName))
                .Select(e => e.name).Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>();

            bool kept = KeepUsed(dir, copyOf.Values);
            var cloth = copyOf.Values.Count(t => t.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "MagicaCloth"));
            report.Converted(Category, $"Converted {BridgeElements.Count(made.Count, "accessory", "accessories")} in place",
                $"{AvatarSurvey.Few(names)}. {BridgeElements.Count(copyOf.Count, "object", "objects")} came back" +
                (cloth > 0 ? $", {cloth} of them cloth" : "") + "." + (kept ? $" Files made for them are in \"{dir}\"." : "") + " Ctrl+Z undoes it.");

            if (menu.Count > 0)
                report.Warning(Category, $"{BridgeElements.Count(menu.Count, "menu entry", "menu entries")} not carried yet",
                    $"{AvatarSurvey.Few(menu)}. Their toggles and animations are not on the avatar yet, so each object stays on or off " +
                    "as the conversion left it.");
        }

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
        static bool KeepUsed(string dir, IEnumerable<Transform> copies)
        {
            var used = new HashSet<string>();
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
