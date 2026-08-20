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
        public const string Twin = "_SelfNotOnHips";

        public class Families
        {
            public readonly SortedDictionary<string, int> Emitted =
                new SortedDictionary<string, int>(StringComparer.Ordinal);
            public readonly SortedDictionary<string, int> Listened =
                new SortedDictionary<string, int>(StringComparer.Ordinal);
            // The shape that forbids collapsing: a trigger accepting the twin
            // and not the base, which collapsing would silence.
            public readonly SortedSet<string> TwinOnly = new SortedSet<string>(StringComparer.Ordinal);

            public int TwinPairs;      // families emitted as base and twin both
            public int TwinPointers;   // pointers those twins account for
            public int Total => Emitted.Values.Sum();
        }

        // The corpus digest calls this too, so the evidence and the thing it
        // is evidence about cannot drift apart.
        public static Families Read(CVRAvatar avatar, Families into = null)
        {
            var f = into ?? new Families();
            if (avatar == null) return f;

            foreach (var p in avatar.GetComponentsInChildren<CVRPointer>(true))
            {
                if (p == null || string.IsNullOrEmpty(p.type)) continue;
                f.Emitted.TryGetValue(p.type, out int n);
                f.Emitted[p.type] = n + 1;
            }

            foreach (var t in avatar.GetComponentsInChildren<CVRAdvancedAvatarSettingsTrigger>(true))
            {
                if (t == null || t.allowedTypes == null) continue;
                var types = new HashSet<string>(t.allowedTypes.Where(x => !string.IsNullOrEmpty(x)),
                    StringComparer.Ordinal);
                foreach (string type in types)
                {
                    f.Listened.TryGetValue(type, out int n);
                    f.Listened[type] = n + 1;

                    if (!type.EndsWith(Twin, StringComparison.Ordinal)) continue;
                    string bare = type.Substring(0, type.Length - Twin.Length);
                    if (!types.Contains(bare)) f.TwinOnly.Add($"{type} on {t.name}");
                }
            }

            f.TwinPairs = 0;
            f.TwinPointers = 0;
            foreach (var pair in f.Emitted)
            {
                if (!pair.Key.EndsWith(Twin, StringComparison.Ordinal)) continue;
                if (!f.Emitted.ContainsKey(pair.Key.Substring(0, pair.Key.Length - Twin.Length))) continue;
                f.TwinPairs++;
                f.TwinPointers += pair.Value;
            }
            return f;
        }

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

            var f = new Families();
            foreach (var avatar in UnityEngine.Object.FindObjectsOfType<CVRAvatar>(true)) Read(avatar, f);

            var sb = new System.Text.StringBuilder();
            sb.Append("# Pointer families in ").Append(target).Append('\n');

            sb.Append("\n## emitted, and whether anything here listens\n");
            foreach (var pair in f.Emitted)
            {
                f.Listened.TryGetValue(pair.Key, out int heard);
                bool twin = pair.Key.EndsWith(Twin, StringComparison.Ordinal);
                sb.Append("  ").Append(pair.Value.ToString().PadLeft(4)).Append("  ")
                  .Append(heard > 0 ? $"heard by {heard}" : "HEARD BY NOTHING HERE").Append("  ")
                  .Append(pair.Key).Append(twin ? "   (twin)" : "").Append('\n');
            }

            sb.Append("\n## triggers accepting a twin but not its base\n");
            sb.Append(f.TwinOnly.Count == 0
                ? "  none: every twin listener also accepts the base, so collapsing the pair is safe here\n"
                : string.Join("\n", f.TwinOnly.Select(t => "  " + t)) + "\n");

            sb.Append("\n## what collapsing would save\n");
            sb.Append("  ").Append(f.TwinPairs).Append(" twin famil(ies), ").Append(f.TwinPointers)
              .Append(" pointers of ").Append(f.Total).Append(" on this avatar\n");

            string repo = Environment.GetEnvironmentVariable("AVATARBRIDGE_REPO") ?? ".";
            File.WriteAllText(Path.Combine(repo, "pointers.md"), sb.ToString());
            Debug.Log(sb.ToString());
            EditorApplication.Exit(0);
        }
    }
}
#endif
