#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // What one "Convert accessories" press dropped and added, saved in its folder: the accessories
    // go back on in one press after the avatar changes, and Detach removes only what the press made.
    // An asset, not a component, since the public package ships no runtime scripts.
    class AccessoryRecord : ScriptableObject
    {
        const string Category = "Accessories";
        const string FileName = "Accessories record.asset";

        [System.Serializable]
        internal class Accessory
        {
            public string prefabGuid, parentPath, name;
            public Vector3 position, scale;
            public Quaternion rotation;
            public bool edited;
        }

        internal struct Dropped
        {
            public string PrefabGuid, ParentPath, Name;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
            public bool Edited;
        }

        public long created;
        public List<Accessory> accessories = new List<Accessory>();
        // Paths beside the ids: an unsaved scene gives its objects no lasting id.
        public List<string> copies = new List<string>(), copyPaths = new List<string>();
        public List<string> swapFrom = new List<string>(), swapTo = new List<string>();
        public List<string> entries = new List<string>();
        // Taken off by Detach or Re-attach. Recorded, so Ctrl+Z puts it back on.
        public bool detached;

        internal static Dropped Capture(GameObject accessory, GameObject target)
        {
            var t = accessory.transform;
            bool prefab = PrefabUtility.IsAnyPrefabInstanceRoot(accessory);
            return new Dropped
            {
                PrefabGuid = prefab ? AssetDatabase.AssetPathToGUID(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(accessory)) : "",
                ParentPath = t.parent == null || t.parent == target.transform ? "" : AnimationUtility.CalculateTransformPath(t.parent, target.transform),
                Name = accessory.name,
                Position = t.localPosition,
                Rotation = t.localRotation,
                Scale = t.localScale,
                Edited = prefab && PrefabUtility.HasPrefabInstanceAnyOverrides(accessory, false),
            };
        }

        internal static string Save(string dir, GameObject target, IList<Dropped> dropped, IEnumerable<GameObject> copies,
            IDictionary<Object, Object> swaps, IEnumerable<string> entries)
        {
            var record = CreateInstance<AccessoryRecord>();
            record.created = System.DateTime.UtcNow.Ticks;
            record.accessories = dropped.Select(d => new Accessory
            {
                prefabGuid = d.PrefabGuid ?? "", parentPath = d.ParentPath ?? "", name = d.Name,
                position = d.Position, rotation = d.Rotation, scale = d.Scale, edited = d.Edited,
            }).ToList();
            foreach (var c in copies.Where(c => c != null))
            {
                record.copies.Add(GlobalObjectId.GetGlobalObjectIdSlow(c).ToString());
                record.copyPaths.Add(AnimationUtility.CalculateTransformPath(c.transform, target.transform));
            }
            if (swaps != null)
                foreach (var s in swaps.Where(s => s.Key != null && s.Value != null))
                {
                    record.swapFrom.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s.Key)));
                    record.swapTo.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s.Value)));
                }
            if (entries != null) record.entries = entries.ToList();
            string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + FileName);
            AssetDatabase.CreateAsset(record, path);
            AssetDatabase.SaveAssets();
            return path;
        }

        internal static List<AccessoryRecord> FindIn(string outputFolder)
        {
            if (string.IsNullOrEmpty(outputFolder) || !AssetDatabase.IsValidFolder(outputFolder)) return new List<AccessoryRecord>();
            return AssetDatabase.FindAssets("t:" + nameof(AccessoryRecord), new[] { outputFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<AccessoryRecord>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(r => r != null).OrderByDescending(r => r.created).ToList();
        }

        // The recorded copies still on this avatar. A copy on another avatar, say the one this
        // replaced, is never touched.
        List<GameObject> Live(GameObject target)
        {
            var found = new List<GameObject>();
            for (int i = 0; i < copies.Count; i++)
            {
                GameObject g = null;
                if (GlobalObjectId.TryParse(copies[i], out var id) && !id.assetGUID.Empty() && id.targetObjectId != 0)
                    g = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as GameObject;
                // ponytail: the path fallback takes the first object of that name, so a same-named
                // sibling the user added could be taken instead. Only for unsaved scenes.
                else if (i < copyPaths.Count) g = Walk(target.transform, copyPaths[i])?.gameObject;
                if (g != null && g.transform.IsChildOf(target.transform) && g != target && !found.Contains(g)) found.Add(g);
            }
            return found;
        }

        static Transform Walk(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            var at = root;
            foreach (var part in path.Split('/'))
            {
                at = at.Cast<Transform>().FirstOrDefault(c => c.name == part);
                if (at == null) return null;
            }
            return at;
        }

        internal static BridgeReport Detach(GameObject target, AccessoryRecord record)
        {
            var report = new BridgeReport();
            if (target == null || record == null) return report;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Detach accessories");
            try
            {
                record.Remove(target, report);
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
            return report;
        }

        void Remove(GameObject target, BridgeReport report)
        {
            Undo.RecordObject(this, "Detach accessories");
            detached = true;
            EditorUtility.SetDirty(this);
            var live = Live(target);
            var names = live.Select(g => g.name).ToList();
            foreach (var g in live) Undo.DestroyObjectImmediate(g);
            int gone = copies.Count - live.Count;

            var back = new Dictionary<Object, Object>();
            for (int i = 0; i < swapFrom.Count && i < swapTo.Count; i++)
            {
                var copy = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(swapTo[i]));
                var original = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(swapFrom[i]));
                if (copy != null && original != null) back[copy] = original;
            }
            bool rewired = back.Count > 0 && PhysBonesInPlace.Wire(target, back);

            int removed = 0;
            var avatar = target.GetComponent<CVRAvatar>();
            // Only with this press's controller copy still wired: after a reconvert the same names
            // are the avatar's own.
            if (rewired && avatar != null && avatar.avatarSettings?.settings != null && entries.Count > 0)
            {
                Undo.RecordObject(avatar, "Detach accessories");
                var drop = new HashSet<string>(entries);
                removed = avatar.avatarSettings.settings.RemoveAll(e => drop.Contains(e.machineName));
                EditorUtility.SetDirty(avatar);
                PrefabUtility.RecordPrefabInstancePropertyModifications(avatar);
            }

            if (live.Count == 0 && !rewired && removed == 0)
            {
                report.Skipped(Category, "Nothing to detach", "What these accessories added is no longer on this avatar.");
                return;
            }
            report.Converted(Category, $"Detached {BridgeElements.Count(live.Count, "object", "objects")}",
                (names.Count > 0 ? AvatarSurvey.Few(names) + ". " : "") +
                (rewired ? "The avatar's own controller is wired back in. " : "") +
                (removed > 0 ? $"{BridgeElements.Count(removed, "menu entry", "menu entries")} removed. " : "") + "Ctrl+Z undoes it.");
            if (gone > 0)
                report.Skipped(Category, $"{BridgeElements.Count(gone, "object was", "objects were")} already gone",
                    "Removed by hand, or the avatar was replaced since.");
            if (entries.Count > removed)
                report.Skipped(Category, $"{BridgeElements.Count(entries.Count - removed, "menu entry was", "menu entries were")} already gone");
        }

        internal static BridgeReport Reattach(GameObject target, AccessoryRecord record, BridgeSettings settings,
            System.Func<BridgeReport, string> folder)
        {
            var report = new BridgeReport();
            if (target == null || record == null) return report;
            if (EditorUtility.IsPersistent(target))
            {
                report.Warning(Category, "This is a prefab asset", "Drag the avatar in from the scene instead.");
                return report;
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Re-attach accessories");
            try
            {
                // Still on, or its controller still wired, from the last press: off first, or they
                // would come twice. Quietly when only the wiring was left.
                record.Remove(target, record.Live(target).Count > 0 ? report : new BridgeReport());
                // Still there unconverted, after Ctrl+Z say: converted where it is, not dropped again.
                var waiting = AccessoryAttach.Find(target);
                int placed = 0;
                foreach (var a in record.accessories)
                {
                    var asset = string.IsNullOrEmpty(a.prefabGuid) ? null
                        : AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(a.prefabGuid));
                    if (asset == null)
                    {
                        report.Warning(Category, $"\"{a.name}\" was not put back", string.IsNullOrEmpty(a.prefabGuid)
                            ? "It was not a prefab when it was dropped, so there is nothing to bring back. Drop it again by hand."
                            : "Its prefab is no longer in the project. Drop it again by hand.");
                        continue;
                    }
                    var parent = Walk(target.transform, a.parentPath);
                    if (parent != null && waiting.Any(w => w.transform.parent == parent && w.name == a.name
                        && AssetDatabase.AssetPathToGUID(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(w)) == a.prefabGuid))
                    {
                        placed++;
                        continue;
                    }
                    if (parent == null)
                    {
                        report.Warning(Category, $"\"{a.name}\" had nowhere to go",
                            $"\"{a.parentPath}\" is not on the avatar, so it was not put back. Drop it again by hand.");
                        continue;
                    }
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
                    g.name = a.name;
                    g.transform.localPosition = a.position;
                    g.transform.localRotation = a.rotation;
                    g.transform.localScale = a.scale;
                    Undo.RegisterCreatedObjectUndo(g, "Re-attach accessories");
                    placed++;
                    if (a.edited)
                        report.Warning(Category, $"\"{a.name}\" is back as its prefab",
                            "It was changed after it was dropped, and those changes are not kept. Make them again if you need them.");
                }
                if (placed == 0) return report;
                report.Entries.AddRange(AccessoryAttach.Run(target, settings, folder).Entries);
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
            return report;
        }
    }
}
#endif
