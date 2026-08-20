// What the texture optimiser would reclaim, across every converted avatar.
//
// Plans only. Nothing is applied, no importer is touched: this answers
// "which avatar has the most to give back" and stops there.
//
//   -executeMethod AvatarBridge.Regression.SlimSurveyProbe.RunBatch
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SlimSurveyProbe
    {
        const string OutputRoot = "Assets/AvatarBridgeOutput";

        class Row
        {
            public string Avatar;
            public long Texture;
            public long Saving;
            public long Download;
            public long DownloadSaving;
            public int Resized;
            public int Reformatted;
            public int Shared;
        }

        public static void RunBatch()
        {
            var rows = new List<Row>();
            string[] folders = AssetDatabase.GetSubFolders(OutputRoot);
            Debug.Log($"[Slim] {folders.Length} converted avatar(s) to read");

            foreach (string folder in folders)
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

                    var survey = AvatarSurvey.Build(avatar);
                    var weight = AvatarWeight.Measure(avatar, survey);
                    var plan = AvatarSlimmer.Find(avatar, survey, weight);

                    var byName = new Dictionary<string, AvatarWeight.TextureUse>(StringComparer.Ordinal);
                    foreach (var use in weight.Textures) byName[use.Name] = use;

                    rows.Add(new Row
                    {
                        Avatar = asset.name,
                        Texture = weight.TextureBytes,
                        Saving = plan.Bytes,
                        Download = weight.DownloadBytes,
                        // A crunched texture already downloads small, so
                        // shrinking it gives the card back, not the wire.
                        DownloadSaving = plan.Textures
                            .Where(p => byName.TryGetValue(p.Name, out var u) && !u.Crunched)
                            .Sum(p => p.Bytes),
                        Resized = plan.Textures.Count(t => t.From != t.To),
                        Reformatted = plan.Textures.Count(t => t.Format.HasValue),
                        Shared = plan.Shared.Count,
                    });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Slim] {asset.name}: {e.Message}");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("# What the texture optimiser would reclaim\n\n");
            sb.Append(rows.Count).Append(" converted avatars.\n\n");
            sb.Append("On the card: ").Append(Mb(rows.Sum(r => r.Texture))).Append(" to ")
              .Append(Mb(rows.Sum(r => r.Texture - r.Saving))).Append('\n');
            sb.Append("To download: ~").Append(Mb(rows.Sum(r => r.Download))).Append(" to ~")
              .Append(Mb(rows.Sum(r => r.Download - r.DownloadSaving))).Append('\n');
            sb.Append("\nDownload is an estimate: the packed size of a crunched texture, the card size\n")
              .Append("of everything else. The CCK settles the real number at upload.\n\n");

            sb.Append("| avatar | card before | card after | download before | download after | resized | reformatted | shared |\n");
            sb.Append("|---|---|---|---|---|---|---|---|\n");
            foreach (var r in rows.OrderByDescending(r => r.Saving))
            {
                sb.Append("| ").Append(r.Avatar)
                  .Append(" | ").Append(Mb(r.Texture))
                  .Append(" | ").Append(Mb(r.Texture - r.Saving))
                  .Append(" | ~").Append(Mb(r.Download))
                  .Append(" | ~").Append(Mb(r.Download - r.DownloadSaving))
                  .Append(" | ").Append(r.Resized)
                  .Append(" | ").Append(r.Reformatted)
                  .Append(" | ").Append(r.Shared)
                  .Append(" |\n");
            }

            string repo = Environment.GetEnvironmentVariable("AVATARBRIDGE_REPO") ?? ".";
            File.WriteAllText(Path.Combine(repo, "slim.md"), sb.ToString());
            Debug.Log($"[Slim] wrote slim.md for {rows.Count} avatar(s)");
            EditorApplication.Exit(0);
        }

        static string Mb(long bytes) => (bytes / 1048576f).ToString("0.0") + " MB";
    }
}
#endif
