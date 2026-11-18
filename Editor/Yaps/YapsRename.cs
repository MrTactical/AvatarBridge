// Renames the objects the SPS bake generates so the hierarchy reads as YAPS.
// A user's own "SPS Plug" stays theirs.
// Not renamed: pointer type strings and marker light ranges (the wire every other
// DPS/TPS/SPS plug finds this avatar by), and parameters (CVR profiles restore saved
// values by name). The hazard is repathing: a missed descendant path fails silently,
// so paths are rewritten by prefix and swept afterwards.

#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsRename
    {
        const string Category = "YAPS";

        // None contains another, so order does not matter.
        static readonly (string From, string To)[] Names =
        {
            ("BakedSpsPlug", "YAPS Plug"),
            ("BakedSpsSocket", "YAPS Socket"),
            ("SpsAutoDistance", "YAPS Auto Distance"),
        };

        public static void Run(BridgeContext ctx)
        {
            if (!ctx.Settings.convertYapsSystems || ctx.MergedController == null)
            {
                return;
            }

            // Old path -> new path, collected before anything moves.
            var remap = new Dictionary<string, string>();
            var doomed = new List<(Transform Target, string To)>();

            foreach (var transform in ctx.Target.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null)
                {
                    continue;
                }
                foreach (var (from, to) in Names)
                {
                    if (YapsScanner.StripFuryId(transform.name) != from)
                    {
                        continue;
                    }
                    // A sibling with (or queued for) the new name would share a path,
                    // and a curve drives whichever one Unity finds first.
                    if (transform.parent != null
                        && (Enumerable.Range(0, transform.parent.childCount)
                                .Select(i => transform.parent.GetChild(i))
                                .Any(c => c != transform && c.name == to)
                            || doomed.Any(d => d.Target.parent == transform.parent && d.To == to)))
                    {
                        ctx.Report.Warning(Category,
                            $"Left \"{from}\" named as it was",
                            $"Renaming it to \"{to}\" would have given it the same name as a " +
                            "object beside it, and two objects sharing a path means an animation " +
                            "reaches whichever one Unity happens to find first. Cosmetic only: " +
                            "nothing about the deform depends on the name.");
                        continue;
                    }
                    remap[ctx.PathInTarget(transform)] = ParentPath(ctx, transform) + to;
                    doomed.Add((transform, to));
                }
            }

            if (doomed.Count == 0)
            {
                return;
            }

            // Sorted once: Remap runs per binding. The fix-up below changes values only.
            var longestFirst = remap.Keys.OrderByDescending(k => k.Length).ToArray();

            // Nested renames run through the parent's NEW name; shortest first so
            // parents settle before children.
            foreach (string key in remap.Keys.OrderBy(k => k.Length).ToList())
            {
                string value = remap[key];
                int cut = value.LastIndexOf('/');
                if (cut < 0)
                {
                    continue;
                }
                if (Remap(value.Substring(0, cut), remap, longestFirst, out string movedParent))
                {
                    remap[key] = movedParent + value.Substring(cut);
                }
            }

            // Answers are fixed now; clips share a few thousand paths.
            var memo = new Dictionary<string, string>();

            // Already dead before the rename (an earlier pass removed them), so not warned on.
            var deadBefore = new HashSet<string>();
            foreach (string path in AnimatedPaths(ctx))
            {
                if (Remap(path, remap, longestFirst, memo, out string moved)
                    && BridgeContext.FindByAnimationPath(ctx.Target.transform, path) == null)
                {
                    deadBefore.Add(moved);
                }
            }

            foreach (var (target, to) in doomed)
            {
                target.name = to;
            }

            int rewritten = RewriteClips(ctx, remap, longestFirst, memo);
            RewriteMasks(ctx, remap, longestFirst, memo);
            var unresolved = Unresolved(ctx, deadBefore);

            if (unresolved.Count > 0)
            {
                // Named, not counted: a bare count leaves nothing to diagnose from.
                var sample = unresolved.Take(12).ToList();
                ctx.Report.Warning(Category,
                    $"{BridgeElements.Count(unresolved.Count, "animation path no longer points", "animation paths no longer point")} at anything",
                    "Some rewritten paths name nothing on this avatar, so what they drove won't move. " +
                    "Please report it at the AvatarBridge repo; it is a bug in the rename. The first few:\n  " +
                    string.Join("\n  ", sample) +
                    (unresolved.Count > sample.Count ? $"\n  … and {unresolved.Count - sample.Count} more" : ""));
            }

            ctx.Report.Converted(Category,
                $"Renamed {BridgeElements.Count(doomed.Count, "object", "objects")} to YAPS",
                $"Named for what drives them now, with {BridgeElements.Count(rewritten, "animation path", "animation paths")} rewritten. Contact " +
                "tags and marker light ranges are unchanged, so DPS, TPS and SPS plugs still find them.");
        }

        static string ParentPath(BridgeContext ctx, Transform transform)
        {
            if (transform.parent == null || transform.parent == ctx.Target.transform)
            {
                return string.Empty;
            }
            return ctx.PathInTarget(transform.parent) + "/";
        }

        // By prefix, so everything beneath a renamed socket moves too.
        static int RewriteClips(BridgeContext ctx, Dictionary<string, string> remap, string[] longestFirst,
            Dictionary<string, string> memo)
        {
            // Batched per clip: each single-curve set resyncs the clip; 3197 bindings took 75 s.
            int count = 0;
            foreach (var clip in Clips(ctx))
            {
                var from = new List<EditorCurveBinding>();
                var to = new List<EditorCurveBinding>();
                var curves = new List<AnimationCurve>();
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!Remap(binding.path, remap, longestFirst, memo, out string path))
                    {
                        continue;
                    }
                    curves.Add(AnimationUtility.GetEditorCurve(clip, binding));
                    from.Add(binding);
                    var moved = binding;
                    moved.path = path;
                    to.Add(moved);
                }
                if (from.Count > 0)
                {
                    AnimationUtility.SetEditorCurves(clip, from.ToArray(), new AnimationCurve[from.Count]);
                    AnimationUtility.SetEditorCurves(clip, to.ToArray(), curves.ToArray());
                    count += from.Count;
                }

                // Material swaps: a separate list, easy to miss.
                from.Clear();
                to.Clear();
                var keys = new List<ObjectReferenceKeyframe[]>();
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (!Remap(binding.path, remap, longestFirst, memo, out string path))
                    {
                        continue;
                    }
                    keys.Add(AnimationUtility.GetObjectReferenceCurve(clip, binding));
                    from.Add(binding);
                    var moved = binding;
                    moved.path = path;
                    to.Add(moved);
                }
                if (from.Count > 0)
                {
                    AnimationUtility.SetObjectReferenceCurves(clip, from.ToArray(), new ObjectReferenceKeyframe[from.Count][]);
                    AnimationUtility.SetObjectReferenceCurves(clip, to.ToArray(), keys.ToArray());
                    count += from.Count;
                }
            }
            return count;
        }

        // A stale mask path silently drops that object's position and scale curves.
        static void RewriteMasks(BridgeContext ctx, Dictionary<string, string> remap, string[] longestFirst,
            Dictionary<string, string> memo)
        {
            var seen = new HashSet<AvatarMask>();
            string controllerPath = AssetDatabase.GetAssetPath(ctx.MergedController);
            foreach (var layer in ctx.MergedController.layers)
            {
                var mask = layer?.avatarMask;
                if (mask == null || !seen.Add(mask))
                {
                    continue;
                }
                // Only masks this conversion owns; never edit a shared asset in place.
                string assetPath = AssetDatabase.GetAssetPath(mask);
                bool ours = string.IsNullOrEmpty(assetPath) || assetPath == controllerPath
                            || assetPath.StartsWith(ctx.OutputDir + "/", StringComparison.Ordinal);
                if (!ours)
                {
                    continue;
                }
                // Serialized and applied once: SetTransformPath per element took a minute.
                var serialized = new SerializedObject(mask);
                var elements = serialized.FindProperty("m_Elements");
                bool touched = false;
                for (int i = 0; i < elements.arraySize; i++)
                {
                    var element = elements.GetArrayElementAtIndex(i).FindPropertyRelative("m_Path");
                    if (Remap(element.stringValue, remap, longestFirst, memo, out string path))
                    {
                        element.stringValue = path;
                        touched = true;
                    }
                }
                if (touched)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(mask);
                }
            }
        }

        static bool Remap(string path, Dictionary<string, string> remap, string[] longestFirst, out string moved)
        {
            moved = null;
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            // Longest first, so a nested rename takes its own new name.
            foreach (string key in longestFirst)
            {
                if (path == key)
                {
                    moved = remap[key];
                    return true;
                }
                // With the separator: "A/Socket" must not match "A/SocketRing".
                if (path.StartsWith(key + "/"))
                {
                    moved = remap[key] + path.Substring(key.Length);
                    return true;
                }
            }
            return false;
        }

        // Null in the memo: no match.
        static bool Remap(string path, Dictionary<string, string> remap, string[] longestFirst,
            Dictionary<string, string> memo, out string moved)
        {
            if (string.IsNullOrEmpty(path))
            {
                moved = null;
                return false;
            }
            if (!memo.TryGetValue(path, out moved))
            {
                Remap(path, remap, longestFirst, out moved);
                memo[path] = moved;
            }
            return moved != null;
        }

        static HashSet<string> AnimatedPaths(BridgeContext ctx)
        {
            var paths = new HashSet<string>();
            foreach (var clip in Clips(ctx))
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    paths.Add(binding.path);
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    paths.Add(binding.path);
                }
            }
            return paths;
        }

        // Proves the rewrite was complete. Only paths naming a renamed object count.
        static List<string> Unresolved(BridgeContext ctx, HashSet<string> deadBefore)
        {
            var missing = new List<string>();
            foreach (string path in AnimatedPaths(ctx).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(path) || deadBefore.Contains(path))
                {
                    continue;
                }
                bool ours = Names.Any(n => path.Contains(n.To));
                // As the animator resolves, not Find: trigger names contain slashes,
                // and Find called 86 working paths dead.
                if (ours && BridgeContext.FindByAnimationPath(ctx.Target.transform, path) == null)
                {
                    missing.Add(path);
                }
            }
            return missing;
        }

        static IEnumerable<AnimationClip> Clips(BridgeContext ctx)
        {
            var seen = new HashSet<AnimationClip>();
            foreach (var clip in ctx.MergedController.animationClips)
            {
                if (clip != null && seen.Add(clip))
                {
                    yield return clip;
                }
            }
        }
    }
}
#endif
