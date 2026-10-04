#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Takes the humanoid Jaw mapping off a bone that is not a jaw and
    // rebuilds the rig. Unity's Auto-Map assigns one to whatever is
    // nearest when it cannot read a face, and the avatar then talks out
    // of it. The baked skeleton passes through as recorded.
    //
    // Always on, no setting: CVR puts the Auto voice position on the jaw
    // bone and jaw visemes animate it, so a misassigned Jaw misplaces the
    // voice and waggles whatever the bone is. Nobody wants that kept.
    public static class JawUnmapper
    {
        const string Category = "Humanoid rig";

        static readonly string[] JawWords = { "jaw", "mandible", "mouth", "kuchi" };
        // Whole words only: as substrings they sit inside "dragon" and "machine".
        static readonly string[] ShortJawWords = { "chin", "ago" };

        public static void Run(BridgeContext ctx)
        {
            var animator = ctx.Target.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                return;
            }
            var jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
            if (jaw == null)
            {
                return;   // nothing mapped, nothing to undo
            }
            if (LooksLikeAJaw(jaw.name))
            {
                ctx.Report.Converted(Category, "Humanoid Jaw kept",
                    $"Mapped to \"{jaw.name}\", which reads as a real jaw: so jaw-bone lip sync has " +
                    "something to drive if this avatar uses it.");
                return;
            }

            // Read from the VRChat descriptor: ctx.CvrAvatar does not exist yet, this pass runs
            // first. CVR drives the humanoid Jaw for jaw-flap lip sync, so unmapping the bone the
            // descriptor moves, whatever its name, is what leaves the mouth dead. With no bone
            // named there, the same placement test the voice uses decides.
            var vrc = ctx.SourceDescriptor;
            bool jawLipSync = vrc != null
                && vrc.lipSync == VRC.SDKBase.VRC_AvatarDescriptor.LipSyncStyle.JawFlapBone;
            bool believable = AvatarFeatureDetect.JawIsBelievable(ctx.Target, animator, jaw, out _);
            if (jawLipSync && (vrc.lipSyncJawBone != null ? ctx.FindInTarget(vrc.lipSyncJawBone) == jaw : believable))
            {
                ctx.Report.Converted(Category, "Humanoid Jaw kept",
                    $"Mapped to \"{jaw.name}\", the bone this avatar's jaw-flap lip sync moves, so it keeps moving.");
                return;
            }

            // What a wrong Jaw costs while it stays mapped. Jaw-flap lip sync moves it, and the
            // Auto voice position sits on any jaw that passes the placement test.
            string cost = jawLipSync
                ? $" This avatar uses JAW-BONE lip sync, so \"{jaw.name}\" is what moves when you " +
                  "speak. Switch it to blendshape visemes on the CVRAvatar, or fix the Jaw mapping."
                : believable
                    ? $" Nothing drives \"{jaw.name}\" for lip sync, but the voice position is placed on it. " +
                      "Check the voice with the CVRAvatar gizmo, or fix the Jaw mapping."
                    : $" This avatar does not use jaw-bone lip sync, so nothing drives \"{jaw.name}\" " +
                      "today, and the voice position is measured from the mouth mesh because the bone " +
                      "sits nowhere a jaw could. The mapping is wrong but currently harmless.";

            var description = animator.avatar.humanDescription;
            var root = animator.gameObject;

            // Drop the Jaw entry. Everything else about the mapping is left exactly alone.
            var human = description.human.Where(h => h.humanName != "Jaw").ToArray();
            if (human.Length == description.human.Length)
            {
                return;   // the mapping is not in the description; nothing this pass can do
            }

            // The baked skeleton passes through, root name and all. Unity
            // wants that name exactly as recorded; renaming it is what
            // makes a rebuild refuse, not the Jaw.
            var skeleton = description.skeleton.ToArray();

            description.human = human;
            description.skeleton = skeleton;

            // Duplicate names are fatal to this call and the message says so plainly: "Ambiguous
            // Transform 'Armature' and 'PhysColliders/Armature' ... must be unique". Caught BEFORE
            // building so the reason reaches the report, rather than only Unity's console.
            var ambiguous = AmbiguousBoneNames(root.transform, description);
            if (ambiguous != null)
            {
                ctx.Report.Skipped(Category,
                    $"Humanoid Jaw is mapped to \"{jaw.name}\", which is not a jaw, and cannot be unmapped",
                    $"\"{ambiguous}\" appears more than once. Rename the duplicate and convert again, or clear the " +
                    "Jaw in the model's Rig > Configure." + cost);
                return;
            }

            // Detached first. With the old rig still assigned,
            // BuildHumanAvatar validates against it rather than the
            // description handed in. The description is the argument;
            // the assigned avatar should not get a vote.
            var previous = animator.avatar;
            animator.avatar = null;
            var rebuilt = AvatarBuilder.BuildHumanAvatar(root, description);

            // Second attempt, from the live hierarchy: the baked skeleton
            // describes the model as imported and conversion has moved
            // on. Second because it carries the current pose, not the
            // configured T-pose.
            bool usedLiveSkeleton = false;
            if (rebuilt == null || !rebuilt.isValid)
            {
                if (rebuilt != null)
                {
                    Object.DestroyImmediate(rebuilt);
                }
                description.skeleton = root.GetComponentsInChildren<Transform>(true)
                    .Select(t => new SkeletonBone
                    {
                        name = t.name,
                        position = t.localPosition,
                        rotation = t.localRotation,
                        scale = t.localScale,
                    }).ToArray();
                rebuilt = AvatarBuilder.BuildHumanAvatar(root, description);
                usedLiveSkeleton = rebuilt != null && rebuilt.isValid;
            }

            if (rebuilt == null || !rebuilt.isValid)
            {
                animator.avatar = previous;   // put it back; a null rig is worse than a wrong jaw
                if (rebuilt != null)
                {
                    Object.DestroyImmediate(rebuilt);
                }
                ctx.Report.Skipped(Category,
                    $"Humanoid Jaw is mapped to \"{jaw.name}\", which is not a jaw, and could not be unmapped",
                    "Unity refused the rebuild. Clear the Jaw in the model's Rig > Configure if you want it " +
                    "gone." + cost +
                    $" (Root \"{root.name}\", rig skeleton " +
                    $"{skeleton.Length} entries from \"{(skeleton.Length > 0 ? skeleton[0].name : "(none)")}\", " +
                    $"then the live hierarchy, {human.Length} human entries. The console says which check failed.)");
                return;
            }

            // Saved, because an Avatar built in memory does not survive the scene: the animator
            // would come back with a null rig and the avatar would stop being humanoid at all.
            //
            // The name comes from "previous"; animator.avatar is null
            // here, cleared before the build.
            rebuilt.name = previous.name;
            string safe = new string(rebuilt.name.Select(c =>
                System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            // Claim, not GenerateUniqueAssetPath. Unique-against-disk
            // includes the last conversion's rig, and reconverting
            // would park numbered copies forever.
            string assetPath = OutputAssetPaths.Claim($"{ctx.OutputDir}/{safe}_NoJaw.asset");
            AssetDatabase.CreateAsset(rebuilt, assetPath);
            int orphans = DeleteStaleRigs(ctx.OutputDir, assetPath);
            animator.avatar = rebuilt;
            EditorUtility.SetDirty(animator);

            ctx.Report.Converted(Category,
                $"Humanoid Jaw unmapped: it pointed at \"{jaw.name}\", which is not a jaw",
                "Rebuilt without a Jaw, so your voice and visemes don't follow that object." +
                (usedLiveSkeleton ? " Rebuilt from the current pose, since the rig's own was refused." : "") +
                (orphans > 0 ? $" {orphans} old rig(s) removed." : ""));
        }

        static int DeleteStaleRigs(string dir, string keep)
        {
            if (!AssetDatabase.IsValidFolder(dir))
            {
                return 0;
            }
            int removed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Avatar", new[] { dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || path == keep)
                {
                    continue;
                }
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                int cut = file.LastIndexOf("_NoJaw", System.StringComparison.Ordinal);
                if (cut < 0)
                {
                    continue;
                }
                string tail = file.Substring(cut + "_NoJaw".Length).Trim();
                if (tail.Length > 0 && !int.TryParse(tail, out _))
                {
                    continue;
                }
                if (AssetDatabase.DeleteAsset(path))
                {
                    removed++;
                }
            }
            return removed;
        }

        static string AmbiguousBoneNames(Transform root, HumanDescription description)
        {
            var wanted = new System.Collections.Generic.HashSet<string>(
                description.human.Select(h => h.boneName).Where(n => !string.IsNullOrEmpty(n)));
            var all = root.GetComponentsInChildren<Transform>(true);
            // Ancestors too: "Armature" is no human bone, yet a duplicate of it refuses the rebuild.
            foreach (var bone in all.Where(t => wanted.Contains(t.name)).ToArray())
            {
                for (var p = bone.parent; p != null && p != root; p = p.parent)
                {
                    wanted.Add(p.name);
                }
            }
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var t in all)
            {
                if (!wanted.Contains(t.name))
                {
                    continue;
                }
                counts.TryGetValue(t.name, out int had);
                counts[t.name] = had + 1;
            }
            return counts.Where(p => p.Value > 1).Select(p => p.Key).FirstOrDefault();
        }

        static bool LooksLikeAJaw(string name)
        {
            // camelCase and digit boundaries split too, so "ChinBone" and "Ago01" still read as words.
            string spaced = Regex.Replace(name,
                "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|(?<=[A-Za-z])(?=[0-9])|(?<=[0-9])(?=[A-Za-z])", " ");
            var words = new string(spaced.ToLowerInvariant()
                    .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
                .Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            string plain = string.Concat(words);
            return JawWords.Any(w => plain.Contains(w)) || ShortJawWords.Any(w => words.Contains(w));
        }
    }
}
#endif
