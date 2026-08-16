#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Census of every animated property that targets a component type conversion deletes or
    // replaces. VRC.* components, DynamicBone; across every AnimationClip in the project.
    //
    // This is the evidence base for the animated-property parity audit. The contact m_Enabled
    // gap (a toggle that switches a contact off in VRChat and silently does nothing converted)
    // sat unnoticed because nothing enumerated what avatars actually animate on the components
    // removed here. Constraints and PhysBones were each handled when a specific avatar broke;
    // this asks the question wholesale instead of waiting for the next tester.
    //
    // Static clips only: VRCFury generates more at bake time, so a zero here is necessary but
    // not sufficient. A non-zero here is a real avatar doing it in the wild.
    public static class AnimatedVrcPropertyScan
    {
        [MenuItem("Tools/AvatarBridge Dev/Scan — animated VRC-component properties")]
        public static void Run()
        {
            var tally = new Dictionary<(string type, string property), (int curves, HashSet<string> clips)>();
            string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
            int scanned = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // The conversion's own output re-animates converted components on purpose;
                // counting it would bury the source-avatar signal this scan exists for.
                if (path.Contains("AvatarBridgeOutput") || path.Contains("/AvatarBridge/"))
                {
                    continue;
                }
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    continue;
                }
                scanned++;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip)
                             .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                {
                    var t = binding.type;
                    if (t == null)
                    {
                        continue;
                    }
                    bool interesting =
                        (t.Namespace != null && t.Namespace.StartsWith("VRC", StringComparison.Ordinal))
                        || t.Name.StartsWith("VRC", StringComparison.Ordinal)
                        || t.Name == "DynamicBone" || t.Name == "DynamicBoneCollider";
                    if (!interesting)
                    {
                        continue;
                    }
                    var key = (t.Name, binding.propertyName);
                    if (!tally.TryGetValue(key, out var entry))
                    {
                        entry = (0, new HashSet<string>());
                    }
                    entry.curves++;
                    if (entry.clips.Count < 4)
                    {
                        entry.clips.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                    }
                    tally[key] = entry;
                }
            }

            Debug.Log($"[VrcPropScan] scanned {scanned} clips; {tally.Count} distinct (type, property) pairs on deleted/replaced components:");
            foreach (var kv in tally.OrderByDescending(k => k.Value.curves))
            {
                Debug.Log($"[VrcPropScan]   {kv.Key.type,-28} {kv.Key.property,-34} curves={kv.Value.curves,-5} e.g. {string.Join(", ", kv.Value.clips)}");
            }
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
    }
}
#endif
