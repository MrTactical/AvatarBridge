#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using ABI.CCK.Components;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// Takes the humanoid Jaw mapping off a bone that is not a jaw, and rebuilds the rig without
    /// it.
    ///
    /// A mis-mapped Jaw is not cosmetic in ChilloutVR. The jaw bone is what the CCK's Auto button
    /// places Voice Position on, and it is what jaw-bone viseme mode animates while you speak — so
    /// an avatar whose Jaw points at a hair strand or a mask talks out of its hair and waggles it
    /// with every syllable. Both avatars in the corpus that map a Jaw at all map it wrongly
    /// ("Mask_L" and "Hair.002"), which is the whole reason this exists: it is not a rare
    /// authoring slip, it is what happens when someone clicks Auto-Map in Unity's avatar
    /// configurator and does not check the face.
    ///
    /// THE REBUILD, and the thing that blocked this for a version:
    ///
    /// The obstacle was this pass. skeleton[0] records the MODEL PREFAB's root as it was at import
    /// — "Gen5Base(Clone)" under a live root called "Cobra" — and that is not staleness to repair,
    /// it is what AvatarBuilder.BuildHumanAvatar expects to be handed back. An earlier revision
    /// read the mismatch as the fault, renamed the entry to the live root, and turned a rebuild
    /// that worked into one Unity refused. Every "Unity refused the rebuild" line this pass printed
    /// was reporting damage it had done itself, and the console message it quoted — "Parent for
    /// 'Armature' differs" — was the consequence, not the cause.
    ///
    /// Settled by building six variants against a real avatar rather than by reasoning about the
    /// message: the unchanged description builds, dropping the Jaw builds, renaming the root
    /// refuses, an empty skeleton refuses, and a skeleton regenerated from the live hierarchy
    /// builds. So the Jaw was never the obstacle, and the rename was the whole of it.
    ///
    /// The rig's own skeleton is therefore passed through untouched, with a regenerated one as a
    /// fallback for descriptions that genuinely have diverged from the hierarchy. That fallback
    /// carries the pose the avatar is standing in rather than the T-pose it was configured in, so
    /// it is second and it says so in the report.
    /// </summary>
    public static class JawUnmapper
    {
        const string Category = "Humanoid rig";

        /// <summary>Names a real jaw goes by. Anything else mapped to Jaw is the accident this
        /// pass exists for — checked as whole words so "Hair.002" cannot match on "ai".</summary>
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

            // What a wrong Jaw actually costs, stated once so both messages below can be honest
            // about it. NOT the voice position: that is measured from the viseme mesh's own
            // vertices, so it lands on the mouth whatever the rig claims. The real exposure is
            // jaw-bone lip sync, which drives the Jaw Close muscle off voice loudness and would
            // waggle whatever this bone happens to be — and only avatars that use that mode.
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

            // The baked skeleton is passed THROUGH, stale-looking root name and all.
            //
            // That name is not stale. skeleton[0] records the model prefab's root as it was at
            // import — "Gen5Base(Clone)" under a live root called "Cobra" — and Unity wants it
            // exactly as recorded. Renaming it to match the live object is what made the rebuild
            // fail, which is worth stating plainly because this pass used to do that deliberately,
            // and every "Unity refused the rebuild" line it printed was describing damage the
            // workaround was doing.
            //
            // Measured rather than reasoned. Six variants were built against a real avatar: the
            // unchanged description builds, dropping the Jaw builds, renaming the root refuses,
            // and a skeleton regenerated from the live hierarchy builds. So the Jaw was never the
            // obstacle and the rename was the whole of it.
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

            // Detached first. With the old rig still assigned, BuildHumanAvatar kept validating
            // against IT rather than against the description handed in: the skeleton root passed
            // in read "Sally_PC (ChilloutVR)" — the live root, correct — and Unity still refused,
            // naming "Sally(Clone)", a string that by then existed nowhere except inside the
            // avatar asset already on the animator. The description is the argument; the assigned
            // avatar should not get a vote.
            var previous = animator.avatar;
            animator.avatar = null;
            var rebuilt = AvatarBuilder.BuildHumanAvatar(root, description);

            // Second attempt with the skeleton regenerated from the live hierarchy. The baked one
            // describes the model as imported, and conversion has since added objects and removed
            // others; where that divergence is what the builder objects to, a skeleton read off
            // the avatar in front of it has no stale entries to object to. Measured as building on
            // a real avatar, so it is a real fallback rather than a hopeful retry.
            //
            // Second, not first: the baked skeleton carries the T-POSE the rig was configured in,
            // and this one carries whatever pose the avatar is standing in now. Identical for an
            // avatar sitting at its bind pose, and a changed rest pose if it is not — worth having
            // when the alternative is keeping a jaw mapped to a hair strand, not worth taking
            // when the baked skeleton was going to work anyway.
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
            // The name comes from "previous", not from animator.avatar, which is NULL here — it
            // was cleared before the build so the old rig could not answer for the description.
            // That line read animator.avatar.name from the day this pass was written and never
            // once threw, because the build never succeeded: every avatar took the early return
            // above. Making the rebuild work ran the success path for the first time, and it
            // failed instantly, taking the whole conversion down with it.
            rebuilt.name = previous.name;
            string safe = new string(rebuilt.name.Select(c =>
                System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            // Claim, not GenerateUniqueAssetPath. "Unique" is the wrong question in the output
            // folder: it compares against everything on disk, INCLUDING the last conversion's
            // rig, so reconverting never reused the name — it parked "…_NoJaw 1.asset" beside
            // it, then " 2", one rig per run, none of them referenced by anything. Every other
            // write site moved off it when OutputAssetPaths was written; this one was missed
            // because the pass had never once reached its success path, so nobody had ever seen
            // it write a second file.
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

        /// <summary>
        /// Removes rigs this pass left in the output folder on earlier runs.
        ///
        /// Claiming the un-numbered name stops NEW copies accumulating, but it cannot reach the
        /// ones already on disk — those keep the numbered names forever, referenced by nothing,
        /// and the folder never shrinks back. Same sweep the restore clips get: match only the
        /// shapes this pass has ever written ("<name>_NoJaw" and "<name>_NoJaw 4"), and keep the
        /// one just written.
        /// </summary>
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

        /// <summary>The first mapped bone name that exists more than once under the root, or null.
        /// Unity resolves humanoid bones by NAME, so a second transform sharing one makes the rig
        /// unbuildable — "PhysColliders/Armature" beside the real "Armature" is the shape of it.</summary>
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
