#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using ABI.CCK.Components;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    // Takes the humanoid Jaw mapping off a bone that is not a jaw and
    // rebuilds the rig. Unity's Auto-Map assigns one to whatever is
    // nearest when it cannot read a face, and the avatar then talks out
    // of it. The baked skeleton passes through as recorded.
    public static class JawUnmapper
    {
        const string Category = "Humanoid rig";

        static readonly string[] JawWords = { "jaw", "chin", "mandible", "mouth", "kuchi", "ago" };

        public static void Run(BridgeContext ctx)
        {
            if (!ctx.Settings.unmapMisplacedJaw)
            {
                return;
            }
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
                    $"Mapped to \"{jaw.name}\", which reads as a real jaw — so jaw-bone lip sync has " +
                    "something to drive if this avatar uses it.");
                return;
            }

            // What a wrong Jaw actually costs. Not the voice position;
            // that is measured from the viseme mesh. The exposure is
            // jaw-bone lip sync, and only avatars using that mode.
            bool jawLipSync = ctx.CvrAvatar != null
                && ctx.CvrAvatar.visemeMode == CVRAvatar.CVRAvatarVisemeMode.JawBone;
            string cost = jawLipSync
                ? $" This avatar uses JAW-BONE lip sync, so \"{jaw.name}\" is what moves when you " +
                  "speak. Switch it to blendshape visemes on the CVRAvatar, or fix the Jaw mapping."
                : $" This avatar does not use jaw-bone lip sync, so nothing drives \"{jaw.name}\" " +
                  "today and the voice position is measured from the mouth mesh rather than the " +
                  "jaw — the mapping is wrong but currently harmless. It would start to matter if " +
                  "you switched this avatar to jaw-bone lip sync.";

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
                    $"Humanoid Jaw is mapped to \"{jaw.name}\", which is not a jaw — and cannot be unmapped",
                    $"Rebuilding the rig needs every mapped bone name to be unique in the hierarchy, " +
                    $"and \"{ambiguous}\" appears more than once. Unity matches humanoid bones BY NAME, " +
                    "so it cannot tell which one the rig means. Rename the duplicate (the copy that " +
                    "is not part of the real armature) and convert again, or clear the Jaw slot " +
                    "yourself in the model's Rig > Configure." + cost);
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
                    $"Humanoid Jaw is mapped to \"{jaw.name}\", which is not a jaw — and could not be unmapped",
                    "Rebuilding the humanoid rig without the Jaw was refused by Unity, so the avatar " +
                    "keeps the mapping it had. Clear the Jaw slot yourself in the model's " +
                    "Rig > Configure if you want it gone." + cost +
                    $" (Tried against root \"{root.name}\" with the rig's own skeleton " +
                    $"({skeleton.Length} entries, root \"{(skeleton.Length > 0 ? skeleton[0].name : "(none)")}\") " +
                    $"and again with one read off the live hierarchy, {human.Length} human entries " +
                    "either way — Unity's own message in the console says which check failed.)");
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
                $"Humanoid Jaw unmapped — it pointed at \"{jaw.name}\", which is not a jaw",
                "The rig is rebuilt without a Jaw. ChilloutVR uses the jaw bone for the Auto voice " +
                "position and for jaw-bone visemes, so a Jaw mapped to hair or a mask puts your voice " +
                "in the wrong place and waggles that object while you speak. With no Jaw at all, the " +
                "voice position falls back to a measured mouth and visemes stay on blendshapes, both " +
                "of which are right." +
                (usedLiveSkeleton
                    ? " The rig's own skeleton was refused, so this was rebuilt from the avatar's " +
                      "live hierarchy instead. That reads the bones where they stand NOW rather " +
                      "than the T-pose the rig was configured in — identical if the avatar is at " +
                      "its bind pose, and a slightly different rest pose if it is not."
                    : "") +
                (orphans > 0
                    ? $" {orphans} rig(s) left in the output folder by earlier conversions were " +
                      "removed; nothing referenced them."
                    : ""));
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
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
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
            string plain = new string(name.ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
            var words = plain.Split(' ');
            return JawWords.Any(w => words.Contains(w))
                   || JawWords.Any(w => plain.Replace(" ", "").Contains(w));
        }
    }
}
#endif
