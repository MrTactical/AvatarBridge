// Which pointer families anything actually listens for.
//
// A socket emits a tag and its _SelfNotOnHips twin, so one socket costs two
// pointers of the instance's 512 pairs. Collapsing the pair is only safe if
// no trigger anywhere listens for the twin ALONE.
//
//   -executeMethod AvatarBridge.Regression.PointerFamilyProbe.RunBatch
//   AVATARBRIDGE_SURVEY_SCENE = the scene or prefab to read
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class PointerFamilyProbe
    {
        const string Twin = "_SelfNotOnHips";

        public static void RunBatch()
        {
            string target = Environment.GetEnvironmentVariable("AVATARBRIDGE_SURVEY_SCENE");
            if (string.IsNullOrEmpty(target))
            {
                Debug.LogError("[Pointers] set AVATARBRIDGE_SURVEY_SCENE");
                EditorApplication.Exit(2);
                return;
            }
            if (target.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(target);
                if (asset != null) PrefabUtility.InstantiatePrefab(asset);
            }
            else
            {
                EditorSceneManager.OpenScene(target, OpenSceneMode.Single);
            }

            var emitted = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var listened = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var twinOnly = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var avatar in UnityEngine.Object.FindObjectsOfType<CVRAvatar>(true))
            {
                foreach (var p in avatar.GetComponentsInChildren<CVRPointer>(true))
                {
                    if (p == null || string.IsNullOrEmpty(p.type)) continue;
                    emitted.TryGetValue(p.type, out int n);
                    emitted[p.type] = n + 1;
                }

                foreach (var t in avatar.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true))
                {
                    if (t == null || t.allowedTypes == null) continue;
                    var types = new HashSet<string>(t.allowedTypes.Where(x => !string.IsNullOrEmpty(x)),
                        StringComparer.Ordinal);
                    foreach (string type in types)
                    {
                        listened.TryGetValue(type, out int n);
                        listened[type] = n + 1;

                        // The dangerous shape: a trigger that accepts the twin
                        // and NOT the base, so collapsing the pair silences it.
                        if (type.EndsWith(Twin, StringComparison.Ordinal))
                        {
                            string bare = type.Substring(0, type.Length - Twin.Length);
                            if (!types.Contains(bare)) twinOnly.Add($"{type} on {t.name}");
                        }
                    }
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("# Pointer families in ").Append(target).Append('\n');

            int pairs = 0, pairCost = 0;
            sb.Append("\n## emitted, and whether anything here listens\n");
            foreach (var pair in emitted)
            {
                listened.TryGetValue(pair.Key, out int heard);
                bool twin = pair.Key.EndsWith(Twin, StringComparison.Ordinal);
                if (twin && emitted.ContainsKey(pair.Key.Substring(0, pair.Key.Length - Twin.Length)))
                {
                    pairs++;
                    pairCost += pair.Value;
                }
                sb.Append("  ").Append(pair.Value.ToString().PadLeft(4)).Append("  ")
                  .Append(heard > 0 ? $"heard by {heard}" : "HEARD BY NOTHING HERE").Append("  ")
                  .Append(pair.Key).Append(twin ? "   (twin)" : "").Append('\n');
            }

            sb.Append("\n## triggers accepting a twin but not its base\n");
            sb.Append(twinOnly.Count == 0
                ? "  none: every twin listener also accepts the base, so collapsing the pair is safe here\n"
                : string.Join("\n", twinOnly.Select(t => "  " + t)) + "\n");

            sb.Append("\n## what collapsing would save\n");
            sb.Append("  ").Append(pairs).Append(" twin famil(ies), ").Append(pairCost)
              .Append(" pointers of ").Append(emitted.Values.Sum()).Append(" on this avatar\n");

            string repo = Environment.GetEnvironmentVariable("AVATARBRIDGE_REPO") ?? ".";
            File.WriteAllText(Path.Combine(repo, "pointers.md"), sb.ToString());
            Debug.Log(sb.ToString());
            EditorApplication.Exit(0);
        }
    }
}
#endif
