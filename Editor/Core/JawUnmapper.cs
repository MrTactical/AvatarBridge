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
    /// AvatarBuilder.BuildHumanAvatar validates the description against the LIVE hierarchy, and a
    /// baked Avatar asset remembers the name the GameObject had when the asset was created. Both
    /// test avatars were configured from a cloned instance, so skeleton[0] reads "Momscarada(Clone)"
    /// against a root now called "Momscarada", and "Sally(Clone)" against a root called "Sally_PC".
    /// Unity then reports the mismatch against the first CHILD it checks — "Parent for 'Armature'
    /// differs ... 'Momscarada(Clone)'" — which reads as a problem with the Armature and is not.
    ///
    /// Note the second case: the base name differs too, so stripping "(Clone)" would produce
    /// "Sally" and fail all over again. The root entry is renamed to whatever the live root is
    /// actually called, which is the only thing that is true in both.
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

            // The root entry, renamed to the live root. See the class comment: a baked Avatar
            // remembers the name its GameObject had when the asset was made, and both test avatars
            // were configured from a clone.
            var skeleton = description.skeleton.ToArray();
            string staleRoot = null;
            if (skeleton.Length > 0 && skeleton[0].name != root.name)
            {
                staleRoot = skeleton[0].name;
                skeleton[0].name = root.name;
            }

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
                    $" (Rebuilt against root \"{root.name}\", skeleton root " +
                    $"\"{(skeleton.Length > 0 ? skeleton[0].name : "(none)")}\"" +
                    $"{(staleRoot != null ? $", renamed from \"{staleRoot}\"" : ", not renamed")}" +
                    $", {skeleton.Length} skeleton and {human.Length} human entries — Unity's own " +
                    "message in the console says which check failed.)");
                return;
            }

            // Saved, because an Avatar built in memory does not survive the scene: the animator
            // would come back with a null rig and the avatar would stop being humanoid at all.
            rebuilt.name = animator.avatar.name;
            string safe = new string(rebuilt.name.Select(c =>
                System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            AssetDatabase.CreateAsset(rebuilt,
                AssetDatabase.GenerateUniqueAssetPath($"{ctx.OutputDir}/{safe}_NoJaw.asset"));
            animator.avatar = rebuilt;
            EditorUtility.SetDirty(animator);

            ctx.Report.Converted(Category,
                $"Humanoid Jaw unmapped — it pointed at \"{jaw.name}\", which is not a jaw",
                "The rig is rebuilt without a Jaw. ChilloutVR uses the jaw bone for the Auto voice " +
                "position and for jaw-bone visemes, so a Jaw mapped to hair or a mask puts your voice " +
                "in the wrong place and waggles that object while you speak. With no Jaw at all, the " +
                "voice position falls back to a measured mouth and visemes stay on blendshapes, both " +
                "of which are right." +
                (staleRoot != null
                    ? $" The rebuild also needed its skeleton root renamed from \"{staleRoot}\" to " +
                      $"\"{root.name}\": a baked humanoid Avatar remembers the object name it was " +
                      "created under, and this one was configured from a clone."
                    : ""));
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
