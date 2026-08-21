// VRCFury's second head, removed, with whatever it carried moved onto the
// head bone.
//
// Fury builds "vrcfAlwaysVisibleHead" as a copy of the head so a VRChat
// player can see their own, because VRChat hides the real one in first
// person. ChilloutVR hides yours by itself, so the copy solves a problem
// this platform does not have — and it arrives switched OFF, waiting for a
// Fury service that gets deleted here.
//
// Anything baked onto it comes off worst. A mouth socket parked there is
// inactive, then animated off by a layer that cannot run, and answers
// nobody. Three separate evenings went into that one socket.
//
// So the copy goes and its children move to the humanoid Head bone, which
// is where a mouth socket belonged in the first place. World positions are
// kept, and every animation path that pointed into the copy is rewritten to
// follow, because a curve addressing a deleted object is a toggle that
// silently stops working.
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class FuryHeadFlattener
    {
        const string Category = "VRCFury";
        const string CopyName = "vrcfAlwaysVisibleHead";

        public static void Run(BridgeContext ctx)
        {
            var copies = ctx.Target.GetComponentsInChildren<Transform>(true)
                .Where(t => t != null && t.name.StartsWith(CopyName, System.StringComparison.Ordinal))
                .ToList();
            if (copies.Count == 0) return;

            var head = ctx.TargetAnimator != null && ctx.TargetAnimator.isHuman
                ? ctx.TargetAnimator.GetBoneTransform(HumanBodyBones.Head)
                : null;
            if (head == null)
            {
                ctx.Report.Approximated(Category, $"{copies.Count} always-visible head(s) left alone",
                    "There is no humanoid Head bone to move their contents onto, so removing them " +
                    "would take whatever they carry with them.");
                return;
            }

            int moved = 0, deleted = 0, repointed = 0;
            var renames = new List<KeyValuePair<string, string>>();
            var names = new List<string>();

            foreach (var copy in copies)
            {
                if (copy == null || copy == head || head.IsChildOf(copy)) continue;

                // Children first, and by index rather than by iterating the
                // live collection: reparenting mutates it as you walk.
                foreach (var child in copy.Cast<Transform>().ToList())
                {
                    string was = ctx.PathInTarget(child);
                    child.SetParent(head, worldPositionStays: true);
                    string now = ctx.PathInTarget(child);
                    if (was != now) renames.Add(new KeyValuePair<string, string>(was, now));
                    if (names.Count < 6) names.Add(child.name);
                    moved++;
                }

                renames.Add(new KeyValuePair<string, string>(ctx.PathInTarget(copy), null));
                Object.DestroyImmediate(copy.gameObject);
                deleted++;
            }

            repointed = Repoint(ctx, renames);

            ctx.Report.Converted(Category, $"{deleted} always-visible head(s) removed",
                (moved > 0
                    ? $"{moved} object(s) moved onto the Head bone — " + string.Join(", ", names) +
                      (moved > names.Count ? ", …" : "") + ". "
                    : "") +
                "VRCFury adds a second head so a VRChat player can see their own; ChilloutVR hides " +
                "yours natively, so the copy has no job here and arrives switched off waiting for a " +
                "service this tool deletes. Anything baked onto it — a mouth socket, most often — " +
                "was off with it and stayed off. It now sits on the head bone it belonged on, at " +
                $"the same place in the world, with {repointed} animation curve(s) repointed to " +
                "follow.");
        }

        // Curves addressing the old paths, moved to the new ones. A rename to
        // null is a delete: the object is gone and the curve with it.
        static int Repoint(BridgeContext ctx, List<KeyValuePair<string, string>> renames)
        {
            if (ctx.MergedController == null || renames.Count == 0) return 0;

            var clips = new HashSet<AnimationClip>();
            foreach (var clip in ctx.MergedController.animationClips)
            {
                if (clip != null) clips.Add(clip);
            }

            int changed = 0;
            foreach (var clip in clips)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    var rename = Match(renames, binding.path);
                    if (rename.Key == null) continue;

                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                    if (rename.Value == null) { changed++; continue; }

                    var moved = binding;
                    moved.path = rename.Value + binding.path.Substring(rename.Key.Length);
                    AnimationUtility.SetEditorCurve(clip, moved, curve);
                    changed++;
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    var rename = Match(renames, binding.path);
                    if (rename.Key == null) continue;

                    var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                    if (rename.Value == null) { changed++; continue; }

                    var moved = binding;
                    moved.path = rename.Value + binding.path.Substring(rename.Key.Length);
                    AnimationUtility.SetObjectReferenceCurve(clip, moved, keys);
                    changed++;
                }
            }
            return changed;
        }

        // The longest old path this binding sits under, so a child's own
        // rename wins over its parent's delete.
        static KeyValuePair<string, string> Match(List<KeyValuePair<string, string>> renames, string path)
        {
            var best = new KeyValuePair<string, string>(null, null);
            foreach (var rename in renames)
            {
                if (path != rename.Key
                    && !path.StartsWith(rename.Key + "/", System.StringComparison.Ordinal))
                {
                    continue;
                }
                if (best.Key == null || rename.Key.Length > best.Key.Length) best = rename;
            }
            return best;
        }
    }
}
#endif
