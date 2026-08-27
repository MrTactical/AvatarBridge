// Make a material-swap animation follow the bake.
//
// Baking repoints a renderer's material slot at a patched copy carrying the
// deform and the baked mesh data. That holds exactly until the animator runs.
// An avatar that assigns a material to that same slot in an animation — a
// skin picker, a body variant, an NSFW toggle — hands the slot straight back
// to the material it was baked FROM, which has neither.
//
// The plug then straightens the instant play mode starts, and the tool
// reports it as never baked, because from the material's side there is
// genuinely nothing left to find. Reported from a converted avatar whose plug
// read "Dick HD 1 _YAPS_" in edit mode and "Dick HD 1" in play mode, the
// shader reverting to the author's Poiyomi alongside it.
//
// Scoped to the renderer AND the slot that was actually repointed. The
// original material is usually worn by other meshes as well, and those have
// no bake of their own: handing them a plug's deform would bend the wrong
// mesh.
//
// Shared because the bug is not the converter's. The native toolkit replaces
// the same slot on the same kind of renderer, and an avatar somebody sets up
// natively is MORE likely to have its toggles already built than one being
// rebuilt from scratch.
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Yaps
{
    public static class YapsSwapFollow
    {
        // "m_Materials.Array.data[3]" -> 3, anything else -> -1.
        public static int SlotIndex(string propertyName)
        {
            const string prefix = "m_Materials.Array.data[";
            if (string.IsNullOrEmpty(propertyName)
                || !propertyName.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                return -1;
            }
            int close = propertyName.IndexOf(']', prefix.Length);
            if (close < 0)
            {
                return -1;
            }
            return int.TryParse(propertyName.Substring(prefix.Length, close - prefix.Length),
                                out int slot) ? slot : -1;
        }

        // The one that does the work. Everything else here just decides which
        // clips to hand it.
        // skipped: clips that matched but are not ours to edit, named so the
        // caller can say so rather than leaving a plug that half works.
        public static int RepointInClips(IEnumerable<AnimationClip> clips, string path,
                                         int slot, Material from, Material to,
                                         ICollection<string> skipped = null)
        {
            if (clips == null || from == null || to == null || from == to || path == null)
            {
                return 0;
            }

            int repointed = 0;
            var seen = new HashSet<AnimationClip>();
            foreach (var clip in clips)
            {
                if (clip == null || !seen.Add(clip))
                {
                    continue;
                }
                // NEVER a clip we do not own. A clip under Packages, or the
                // CCK's, or one of ours, is shared with every project that
                // has it: editing in place reaches the package file itself.
                // The native path walks the user's LIVE controllers, so this
                // is not hypothetical the way it was for the converter, whose
                // clips are already its own clones.
                bool ours = YapsCurveMirror.UserOwned(clip);
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (binding.path != path || SlotIndex(binding.propertyName) != slot)
                    {
                        continue;
                    }
                    var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    if (keys == null)
                    {
                        continue;
                    }
                    bool touched = false;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (keys[i].value != from)
                        {
                            continue;
                        }
                        if (!ours)
                        {
                            // Matched, and left alone. Silently skipping is
                            // what produces a plug that works until somebody
                            // presses the one toggle nobody thought to try.
                            skipped?.Add(clip.name);
                            break;
                        }
                        keys[i].value = to;
                        touched = true;
                        repointed++;
                    }
                    if (touched)
                    {
                        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
                        EditorUtility.SetDirty(clip);
                    }
                }
            }
            return repointed;
        }

        // What animation paths in this avatar's clips are relative to. For a
        // ChilloutVR avatar that is the CVRAvatar's own transform, which is
        // also where the Animator sits.
        public static Transform AnimationRootOf(Transform any)
        {
            if (any == null) return null;
            var avatar = any.GetComponentInParent<CVRAvatar>(true);
            if (avatar != null) return avatar.transform;
            var spawnable = any.GetComponentInParent<CVRSpawnable>(true);
            if (spawnable != null) return spawnable.transform;
            var animator = any.GetComponentInParent<Animator>(true);
            return animator != null ? animator.transform : any.root;
        }

        // EVERY clip this avatar could run, not just the Animator's.
        //
        // ChilloutVR uploads what avatar.overrides points at and falls back to
        // avatarSettings.baseController; the Animator's own slot holds a
        // generated override that is not what ships, and on an avatar that has
        // never been built it is often empty. Reading only that slot means
        // finding nothing at all on a perfectly ordinary avatar, and doing
        // nothing quietly.
        //
        // All three are read, because a clip only has to be REACHABLE to fire.
        // Unfiltered: the caller decides what it is allowed to write to, and
        // one caller wants to report the ones it must leave alone.
        public static List<AnimationClip> RunnableClips(Transform any)
        {
            var root = AnimationRootOf(any);
            var clips = new List<AnimationClip>();
            if (root == null) return clips;

            void Take(RuntimeAnimatorController runtime)
            {
                if (runtime != null && runtime.animationClips != null)
                {
                    clips.AddRange(runtime.animationClips.Where(c => c != null));
                }
            }
            var avatar = any.GetComponentInParent<CVRAvatar>(true);
            if (avatar != null)
            {
                Take(avatar.overrides);
                if (avatar.avatarSettings != null) Take(avatar.avatarSettings.baseController);
            }
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                Take(animator.runtimeAnimatorController);
            }
            return clips.Distinct().ToList();
        }

        // The native path: no merged controller to read, so the clips come off
        // whatever the avatar or prop actually runs.
        //
        // ChilloutVR uploads what avatar.overrides points at and falls back to
        // avatarSettings.baseController; the Animator's own slot holds a
        // generated override that is not what ships. Every one of them is read
        // here anyway, because a clip only has to be reachable to fire, and a
        // swap that fires from the wrong controller breaks the plug just the
        // same.
        public static int Follow(Renderer renderer, int slot, Material from, Material to,
                                 BridgeReport report = null)
        {
            if (renderer == null || from == null || to == null || from == to)
            {
                return 0;
            }

            Transform root = AnimationRootOf(renderer.transform);
            var clips = RunnableClips(renderer.transform);
            if (root == null || clips.Count == 0)
            {
                return 0;
            }

            string path = AnimationUtility.CalculateTransformPath(renderer.transform, root);
            var skipped = new SortedSet<string>();
            int repointed = RepointInClips(clips, path, slot, from, to, skipped);
            if (skipped.Count > 0 && report != null)
            {
                report.Warning("YAPS",
                    $"{skipped.Count} material swap(s) could not be repointed",
                    "These animations assign a material to the mesh slot the bake replaced, and " +
                    "they live outside your Assets folder — in a package, or in the CCK — so " +
                    "editing them would change them for every project that has them. Playing one " +
                    "will put the unbaked material back and the plug will stop bending. Copy the " +
                    "clip into your own project and point it at the baked material: "
                    + string.Join(", ", skipped));
            }
            if (repointed > 0 && report != null)
            {
                report.Converted("YAPS",
                    $"Pointed {repointed} material swap(s) at the baked material",
                    "An animation on this avatar assigns a material to the same mesh slot the " +
                    "bake replaced. Left alone it hands the slot back to the material you baked " +
                    "FROM, which carries no deform, so the plug straightens the moment you press " +
                    "Play and reads as never baked. Only the exact mesh and slot the bake touched " +
                    "was changed; anything else wearing that material keeps it.");
            }
            return repointed;
        }
    }
}
