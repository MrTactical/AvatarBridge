// How many cloth solvers could actually merge.
//
// The weight card says several solvers under one parent "could merge —
// unless a toggle switches them apart". That is one gate and there are two,
// because a MagicaCloth holds ONE set of simulation settings for every root
// bone it carries:
//
//   1. nothing may switch two solvers apart, or merging welds a toggle shut
//   2. their settings must already match, or merging retunes the loser
//
// So this counts the ceiling honestly: solvers, minus the distinct
// (toggle signature, settings fingerprint) pairs they fall into. Reads only.
//
//   -executeMethod AvatarBridge.Regression.ClothMergeProbe.RunBatch
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class ClothMergeProbe
    {
        const string OutputRoot = "Assets/AvatarBridgeOutput";
        const string MagicaClothType = "MagicaCloth2.MagicaCloth";

        class Group
        {
            public string Parent;
            public int Solvers;
            public int ByToggle;      // distinct toggle signatures
            public int BySettings;    // distinct settings fingerprints
            public int Distinct;      // distinct pairs of the two
            public int Mergeable => Solvers - Distinct;
        }

        class Row
        {
            public string Avatar;
            public int Solvers;
            public readonly List<Group> Groups = new List<Group>();
            public int Mergeable => Groups.Sum(g => g.Mergeable);
        }

        public static void RunBatch()
        {
            var rows = new List<Row>();
            foreach (string folder in AssetDatabase.GetSubFolders(OutputRoot))
            {
                string prefab = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));
                if (prefab == null) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                if (asset == null) continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                try
                {
                    var avatar = instance.GetComponent<CVRAvatar>();
                    if (avatar == null) continue;
                    var row = Check(avatar, asset.name);
                    if (row != null && row.Solvers > 0) rows.Add(row);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Cloth] {asset.name}: {e.Message}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            Write(rows);
            EditorApplication.Exit(0);
        }

        static Row Check(CVRAvatar avatar, string name)
        {
            var solvers = avatar.GetComponentsInChildren<Component>(true)
                .Where(c => c != null
                            && (c.GetType().FullName == MagicaClothType || c.GetType().Name == "DynamicBone"))
                .ToList();
            if (solvers.Count == 0) return null;

            var switching = SwitchingByPath(avatar);
            var row = new Row { Avatar = name, Solvers = solvers.Count };

            foreach (var byParent in solvers.GroupBy(c => c.transform.parent != null
                         ? c.transform.parent.name : "<root>"))
            {
                var members = byParent.ToList();
                if (members.Count < 2) continue;

                var toggles = new List<string>();
                var settings = new List<string>();
                foreach (var c in members)
                {
                    string path = AnimationUtility.CalculateTransformPath(c.transform, avatar.transform);
                    toggles.Add(Signature(path, switching));
                    settings.Add(Fingerprint(c));
                }

                row.Groups.Add(new Group
                {
                    Parent = byParent.Key,
                    Solvers = members.Count,
                    ByToggle = toggles.Distinct().Count(),
                    BySettings = settings.Distinct().Count(),
                    Distinct = Enumerable.Range(0, members.Count)
                        .Select(i => toggles[i] + " | " + settings[i]).Distinct().Count(),
                });
            }
            return row;
        }

        // Every clip that can switch a given path, by name, so two solvers
        // driven by the same clips read identically and two driven by
        // different ones do not.
        static Dictionary<string, SortedSet<string>> SwitchingByPath(CVRAvatar avatar)
        {
            var map = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var animator in avatar.GetComponentsInChildren<Animator>(true))
            {
                var controller = BridgeContext.Underlying(animator.runtimeAnimatorController);
                if (controller == null) continue;
                foreach (var clip in controller.animationClips)
                {
                    if (clip == null) continue;
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (b.propertyName != "m_Enabled" && b.propertyName != "m_IsActive") continue;
                        if (!map.TryGetValue(b.path, out var set))
                        {
                            set = new SortedSet<string>(StringComparer.Ordinal);
                            map[b.path] = set;
                        }
                        set.Add(clip.name + "::" + b.propertyName);
                    }
                }
            }
            return map;
        }

        // The path itself and every ancestor, since switching a parent takes
        // everything under it.
        static string Signature(string path, Dictionary<string, SortedSet<string>> switching)
        {
            var all = new SortedSet<string>(StringComparer.Ordinal);
            if (switching.TryGetValue(path, out var own)) all.UnionWith(own);
            for (int cut = path.LastIndexOf('/'); cut > 0; cut = path.LastIndexOf('/', cut - 1))
            {
                if (switching.TryGetValue(path.Substring(0, cut), out var up)) all.UnionWith(up);
            }
            return all.Count == 0 ? "<never switched>" : string.Join(",", all);
        }

        // Everything serialized except what names the bones. Two solvers can
        // only merge into one if the settings they would share already agree.
        static string Fingerprint(Component c)
        {
            var so = new SerializedObject(c);
            var p = so.GetIterator();
            var sb = new System.Text.StringBuilder();
            while (p.NextVisible(true))
            {
                string lower = p.propertyPath.ToLowerInvariant();
                if (lower.Contains("root") || lower.Contains("m_script") || lower.Contains("collider")
                    || lower.Contains("name") || lower.Contains("transform"))
                {
                    continue;
                }
                sb.Append(p.propertyPath).Append('=');
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Float: sb.Append(p.floatValue.ToString("0.####")); break;
                    case SerializedPropertyType.Integer: sb.Append(p.intValue); break;
                    case SerializedPropertyType.Boolean: sb.Append(p.boolValue); break;
                    case SerializedPropertyType.Enum: sb.Append(p.enumValueIndex); break;
                    case SerializedPropertyType.Vector3: sb.Append(p.vector3Value); break;
                    case SerializedPropertyType.AnimationCurve: sb.Append(Curve(p)); break;
                    default: continue;
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        static string Curve(SerializedProperty p)
        {
            var curve = p.animationCurveValue;
            if (curve == null || curve.keys == null) return "<none>";
            return string.Join("/", curve.keys.Select(k => $"{k.time:0.###}:{k.value:0.###}"));
        }

        static void Write(List<Row> rows)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("# How many cloth solvers could actually merge\n\n");

            int solvers = rows.Sum(r => r.Solvers);
            int mergeable = rows.Sum(r => r.Mergeable);
            sb.Append(rows.Count).Append(" avatars, ").Append(solvers).Append(" solvers.\n");
            sb.Append("Could merge away: ").Append(mergeable)
              .Append(solvers > 0 ? $"  ({100f * mergeable / solvers:0}%)" : "").Append('\n');
            sb.Append("\nA merge needs BOTH: nothing switching the two apart, and settings that already\n")
              .Append("agree. One MagicaCloth holds one set of settings for every root bone it carries.\n\n");

            sb.Append("| avatar | solvers | could merge | crowded parents |\n");
            sb.Append("|---|---|---|---|\n");
            foreach (var r in rows.Where(r => r.Groups.Count > 0).OrderByDescending(r => r.Mergeable))
            {
                sb.Append("| ").Append(r.Avatar)
                  .Append(" | ").Append(r.Solvers)
                  .Append(" | ").Append(r.Mergeable)
                  .Append(" | ").Append(r.Groups.Count)
                  .Append(" |\n");
            }

            sb.Append("\n## the crowded parents, and what stops them\n\n");
            sb.Append("| avatar | parent | solvers | toggle groups | settings groups | could merge |\n");
            sb.Append("|---|---|---|---|---|---|\n");
            foreach (var r in rows)
            {
                foreach (var g in r.Groups.OrderByDescending(g => g.Solvers).Take(4))
                {
                    sb.Append("| ").Append(r.Avatar)
                      .Append(" | ").Append(g.Parent)
                      .Append(" | ").Append(g.Solvers)
                      .Append(" | ").Append(g.ByToggle)
                      .Append(" | ").Append(g.BySettings)
                      .Append(" | ").Append(g.Mergeable)
                      .Append(" |\n");
                }
            }

            string repo = Environment.GetEnvironmentVariable("AVATARBRIDGE_REPO") ?? ".";
            File.WriteAllText(Path.Combine(repo, "cloth.md"), sb.ToString());
            Debug.Log($"[Cloth] wrote cloth.md: {solvers} solver(s), {mergeable} mergeable");
        }
    }
}
#endif
