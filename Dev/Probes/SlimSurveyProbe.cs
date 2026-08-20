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
            public int Textures;
            public int Resized;
            public int Compressed;
            public int Reformatted;
            public int Shared;
            public double Share => Texture > 0 ? (double)Saving / Texture : 0;
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

                    rows.Add(new Row
                    {
                        Avatar = asset.name,
                        Texture = weight.TextureBytes,
                        Saving = plan.Bytes,
                        Textures = weight.Textures.Count,
                        Resized = plan.Textures.Count(t => t.From != t.To),
                        Compressed = plan.Textures.Count(t => t.Compress),
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
            sb.Append(rows.Count).Append(" converted avatars, ")
              .Append(Mb(rows.Sum(r => r.Texture))).Append(" of texture between them, ")
              .Append(Mb(rows.Sum(r => r.Saving))).Append(" reclaimable\n\n");

            sb.Append("## by how much comes off\n\n");
            sb.Append("| avatar | texture | reclaimable | share | resized | compressed | reformatted | shared |\n");
            sb.Append("|---|---|---|---|---|---|---|---|\n");
            foreach (var r in rows.OrderByDescending(r => r.Saving))
            {
                sb.Append("| ").Append(r.Avatar)
                  .Append(" | ").Append(Mb(r.Texture))
                  .Append(" | ").Append(Mb(r.Saving))
                  .Append(" | ").Append((r.Share * 100).ToString("0")).Append("%")
                  .Append(" | ").Append(r.Resized)
                  .Append(" | ").Append(r.Compressed)
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
