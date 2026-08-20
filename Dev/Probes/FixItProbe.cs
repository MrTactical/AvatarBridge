// Weigh, fix, weigh again, on one avatar.
//
// The card and the plan agreeing proves nothing: what matters is whether
// the texture on disk actually changed. This reports the measured size
// either side of Apply.
//
//   -executeMethod AvatarBridge.Regression.FixItProbe.Run
#if CVR_CCK_EXISTS
using System;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class FixItProbe
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

            var before = AvatarWeight.Measure(avatar, AvatarSurvey.Build(avatar));
            var plan = AvatarSlimmer.Find(avatar, AvatarSurvey.Build(avatar), before);
            Debug.Log($"[Fix] before: {(before.TextureBytes / 1048576f):0.0}MB, " +
                      $"plan says {(plan.Bytes / 1048576f):0.0}MB off, {plan.Textures.Count} texture(s)");
            foreach (var t in plan.Textures)
            {
                Debug.Log($"[Fix]   plan {t.Name}: {t.From} -> {t.To}" +
                          (t.Format.HasValue ? $", format {t.Format}" : ""));
            }

            var report = new BridgeReport();
            AvatarSlimmer.Apply(avatar, plan, folder, report);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var after = AvatarWeight.Measure(avatar, AvatarSurvey.Build(avatar));
            Debug.Log($"[Fix] after: {(after.TextureBytes / 1048576f):0.0}MB " +
                      $"(actually off: {((before.TextureBytes - after.TextureBytes) / 1048576f):0.0}MB)");

            foreach (var t in plan.Textures)
            {
                var now = after.Textures.FirstOrDefault(x => x.Name == t.Name);
                if (now == null) continue;
                bool took = Mathf.Max(now.Width, now.Height) <= t.To;
                Debug.Log($"[Fix]   {t.Name}: now {now.Width}x{now.Height} {now.Format} " +
                          $"{(took ? "OK" : "DID NOT RESIZE")}");
            }

            UnityEngine.Object.DestroyImmediate(instance);
            EditorApplication.Exit(0);
        }
    }
}
#endif
