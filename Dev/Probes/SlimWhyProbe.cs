// Why a texture is not in the plan.
//
// The card names a texture as worth shrinking and Fix it leaves it alone:
// this prints the reason for each one, from the same numbers the planner
// reads.
//
//   -executeMethod AvatarBridge.Regression.SlimWhyProbe.Run
#if CVR_CCK_EXISTS
using System;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SlimWhyProbe
    {
        public static void Run()
        {
            string folder = Environment.GetEnvironmentVariable("AVATARBRIDGE_ONE");
            string prefab = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            var avatar = instance.GetComponent<CVRAvatar>();

            var survey = AvatarSurvey.Build(avatar);
            var weight = AvatarWeight.Measure(avatar, survey);
            var plan = AvatarSlimmer.Find(avatar, survey, weight);

            Debug.Log($"[Why] {asset.name}: {weight.Textures.Count} textures, plan has " +
                      $"{plan.Textures.Count} shrink(s), {plan.Shared.Count} shared, {plan.Strip.Count} strip");

            foreach (var t in weight.Textures.OrderByDescending(t => t.Bytes))
            {
                string path = AssetDatabase.GetAssetPath(t.Texture);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                int longest = Mathf.Max(t.Width, t.Height);
                string verdict;

                if (plan.Textures.Any(p => p.Name == t.Name)) verdict = "IN PLAN";
                else if (plan.Shared.Contains(t.Name)) verdict = "SHARED with a material outside this avatar";
                else if (importer == null) verdict = $"no TextureImporter ({System.IO.Path.GetExtension(path)})";
                else if (t.Data) verdict = "data texture, left alone";
                else if (t.Suggested <= 0) verdict = "no suggestion (no measured surface)";
                else if (t.Suggested >= longest) verdict = $"already at or under target ({t.Suggested} vs {longest})";
                else if (importer.maxTextureSize <= t.Suggested)
                    verdict = $"importer maxTextureSize {importer.maxTextureSize} already <= suggested {t.Suggested}";
                else verdict = "below the quarter-meg floor";

                Debug.Log($"[Why]   {t.Name} | {t.Width}x{t.Height} {t.Format} | " +
                          $"{(t.Bytes / 1048576f):0.00}MB | suggest {t.Suggested} | " +
                          $"importerMax {(importer != null ? importer.maxTextureSize : -1)} | {verdict}");
            }

            UnityEngine.Object.DestroyImmediate(instance);
            EditorApplication.Exit(0);
        }
    }
}
#endif
