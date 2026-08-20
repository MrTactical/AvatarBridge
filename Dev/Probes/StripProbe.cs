// Does stripping a hidden mesh hold up when it actually runs?
//
// The analysis is proven across the corpus; this is about the edit. It
// strips for real on a prefab INSTANCE, so the asset on disk is never
// touched, then asks three things: did the objects survive, did whatever
// hung off them survive, and is the saving the one that was promised.
//
//   -executeMethod AvatarBridge.Regression.StripProbe.RunBatch
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
    public static class StripProbe
    {
        const string OutputRoot = "Assets/AvatarBridgeOutput";

        class Row
        {
            public string Avatar;
            public int Stripped;
            public long Promised;
            public long Delivered;
            public int RenderersBefore;
            public int RenderersAfter;
            public int TrianglesFreed;
            public readonly List<string> Lost = new List<string>();
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
                    if (row != null) rows.Add(row);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Strip] {asset.name}: {e.Message}");
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
            var survey = AvatarSurvey.Build(avatar);
            var before = AvatarWeight.Measure(avatar, survey);
            if (before.Dead.Count == 0) return null;

            var plan = AvatarSlimmer.Find(avatar, survey, before);
            // Textures are a separate question and their settings are global
            // to the asset. This is only about the renderers.
            plan.Textures.Clear();

            var row = new Row
            {
                Avatar = name,
                Promised = plan.StripBytes,
                RenderersBefore = before.Renderers,
            };

            // Everything hanging off each doomed renderer, to look for after.
            var expected = new List<string>();
            foreach (string path in plan.Strip)
            {
                var found = BridgeContext.FindByAnimationPath(avatar.transform, path);
                if (found == null) continue;
                foreach (var t in found.GetComponentsInChildren<Transform>(true))
                {
                    expected.Add(AnimationUtility.CalculateTransformPath(t, avatar.transform));
                }
                foreach (var c in found.GetComponents<Component>())
                {
                    if (c == null || c is Renderer || c is MeshFilter || c is Transform) continue;
                    expected.Add(path + " :: " + c.GetType().Name);
                }
            }

            var report = new BridgeReport();
            AvatarSlimmer.Apply(avatar, plan, null, report);

            var after = AvatarWeight.Measure(avatar, AvatarSurvey.Build(avatar));
            row.RenderersAfter = after.Renderers;
            row.Stripped = before.Renderers - after.Renderers;
            row.Delivered = before.TextureBytes - after.TextureBytes;
            row.TrianglesFreed = before.Triangles - after.Triangles;

            // Whatever sat under a stripped renderer should still be there.
            // Taking the object rather than the component is what would
            // show up here.
            foreach (string want in expected)
            {
                int mark = want.IndexOf(" :: ", StringComparison.Ordinal);
                if (mark < 0)
                {
                    if (BridgeContext.FindByAnimationPath(avatar.transform, want) == null) row.Lost.Add(want);
                    continue;
                }
                var host = BridgeContext.FindByAnimationPath(avatar.transform, want.Substring(0, mark));
                if (host == null) { row.Lost.Add(want); continue; }
                string type = want.Substring(mark + 4);
                if (!host.GetComponents<Component>().Any(c => c != null && c.GetType().Name == type))
                {
                    row.Lost.Add(want);
                }
            }
            return row;
        }

        static void Write(List<Row> rows)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("# Stripping hidden meshes, for real\n\n");

            int lost = rows.Sum(r => r.Lost.Count);
            long promised = rows.Sum(r => r.Promised);
            long delivered = rows.Sum(r => r.Delivered);

            sb.Append(rows.Count).Append(" avatars with something to strip, ")
              .Append(rows.Sum(r => r.Stripped)).Append(" renderers removed, ")
              .Append(rows.Sum(r => r.TrianglesFreed).ToString("N0")).Append(" triangles freed.\n\n");
            sb.Append("Promised: ").Append(Mb(promised)).Append('\n');
            sb.Append("Delivered: ").Append(Mb(delivered))
              .Append(promised == delivered ? "  (they agree)" : "  THEY DISAGREE").Append('\n');
            sb.Append("Objects or components lost under a stripped renderer: ").Append(lost)
              .Append(lost == 0 ? "  (nothing went with it)" : "  SOMETHING WENT WITH IT").Append('\n');

            if (lost > 0)
            {
                sb.Append("\n## Lost\n\n");
                foreach (var r in rows.Where(r => r.Lost.Count > 0))
                {
                    sb.Append("- **").Append(r.Avatar).Append("**\n");
                    foreach (string p in r.Lost.Take(20)) sb.Append("  - ").Append(p).Append('\n');
                }
            }

            sb.Append("\n| avatar | stripped | renderers | promised | delivered | triangles |\n");
            sb.Append("|---|---|---|---|---|---|\n");
            foreach (var r in rows.OrderByDescending(r => r.Delivered))
            {
                sb.Append("| ").Append(r.Avatar)
                  .Append(" | ").Append(r.Stripped)
                  .Append(" | ").Append(r.RenderersBefore).Append(" to ").Append(r.RenderersAfter)
                  .Append(" | ").Append(Mb(r.Promised))
                  .Append(" | ").Append(Mb(r.Delivered))
                  .Append(r.Promised == r.Delivered ? "" : "  MISMATCH")
                  .Append(" | ").Append(r.TrianglesFreed.ToString("N0"))
                  .Append(" |\n");
            }

            string repo = Environment.GetEnvironmentVariable("AVATARBRIDGE_REPO") ?? ".";
            File.WriteAllText(Path.Combine(repo, "strip.md"), sb.ToString());
            Debug.Log($"[Strip] wrote strip.md: {rows.Count} avatar(s), {lost} lost");
        }

        static string Mb(long bytes) => (bytes / 1048576f).ToString("0.0") + " MB";
    }
}
#endif
