// Every socket on a converted avatar, and everything that decides whether
// it answers a plug.
//
// Three avatars showed three behaviours from the same build: one dead both
// ways, one dead only for the wearer, one fine. That is conditional, so
// this prints the conditions rather than a verdict:
//
//   active    an inactive socket answers nobody, local or remote
//   tags      the base tag is what another player's plug reads; the
//             _SelfNotOnHips twin is the channel the WEARER's own plug
//             reads, and a socket missing it works remotely and not locally
//   lights    range % 0.1 is the message a DPS plug decodes: 0.01 hole,
//             0.02 ring, 0.05 front
//
//   -executeMethod AvatarBridge.Regression.SocketAuditProbe.Run
//   AVATARBRIDGE_ONE = the output folder holding the prefab
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SocketAuditProbe
    {
        public static void Run()
        {
            string folder = Environment.GetEnvironmentVariable("AVATARBRIDGE_ONE");
            string prefab = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase));
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
            if (asset == null)
            {
                Debug.LogError($"[Socket] no prefab in {folder}");
                EditorApplication.Exit(2);
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            var avatar = instance.GetComponent<CVRAvatar>();
            Debug.Log($"[Socket] {asset.name}");

            // Every pointer on the avatar, by the object that carries it, so
            // a socket can be asked what it broadcasts.
            var pointers = instance.GetComponentsInChildren<CVRPointer>(true);
            Debug.Log($"[Socket] {pointers.Length} pointer(s) on this avatar");

            var families = pointers
                .Where(p => p != null && !string.IsNullOrEmpty(p.type))
                .GroupBy(p => p.type)
                .OrderBy(g => g.Key, StringComparer.Ordinal);
            foreach (var f in families)
            {
                bool twin = f.Key.EndsWith("_SelfNotOnHips", StringComparison.Ordinal);
                string bare = twin ? f.Key.Substring(0, f.Key.Length - 14) : f.Key;
                bool hasTwin = twin || pointers.Any(p =>
                    p != null && p.type == bare + "_SelfNotOnHips");
                Debug.Log($"[Socket]   {f.Count():D2} {f.Key}" +
                          (twin ? "" : hasTwin ? "   (self twin present)" : "   NO SELF TWIN"));
            }

            // Each socket branch: is it switched on, and what does it carry?
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                var mine = t.GetComponents<CVRPointer>();
                if (mine.Length == 0) continue;
                var kinds = mine.Where(p => p != null && !string.IsNullOrEmpty(p.type))
                    .Select(p => p.type).ToList();
                if (!kinds.Any(k => k.Contains("Socket") || k.Contains("Orf"))) continue;

                string path = AnimationUtility.CalculateTransformPath(t, instance.transform);
                bool self = kinds.Any(k => k.EndsWith("_SelfNotOnHips", StringComparison.Ordinal));
                Debug.Log($"[Socket] {(t.gameObject.activeInHierarchy ? "ON " : "OFF")} " +
                          $"{(self ? "self+remote" : "REMOTE ONLY")}  {path}");
                Debug.Log($"[Socket]      tags: {string.Join(", ", kinds)}");
            }

            // The lights, by the digit a decoder reads out of them.
            foreach (var light in instance.GetComponentsInChildren<Light>(true))
            {
                if (light == null || light.type != LightType.Point) continue;
                if (light.range <= 0.05f || light.range >= 0.5f) continue;
                float frac = light.range % 0.1f;
                Debug.Log($"[Socket] light range {light.range:0.0000}  frac {frac:0.0000}  " +
                          $"reads as {Mathf.RoundToInt(frac * 100f)}  " +
                          $"{(light.enabled && light.gameObject.activeInHierarchy ? "lit" : "DARK")}  " +
                          AnimationUtility.CalculateTransformPath(light.transform, instance.transform));
            }

            UnityEngine.Object.DestroyImmediate(instance);
            EditorApplication.Exit(0);
        }
    }
}
#endif
