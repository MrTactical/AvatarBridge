// Make a material-swap animation follow the bake.
//
// Baking repoints a renderer's material slot at a patched copy carrying
// the deform and the baked mesh data. That holds exactly until the
// animator runs. An avatar that assigns a material to that same slot in
// an animation, a skin picker, a body variant, a toggle, hands the slot
// straight back to the material it was baked FROM, which has neither.
//
// The plug then straightens the instant play mode starts, and the tool
// reports it as never baked, because from the material's side there is
// nothing left to find. In edit mode the patched copy is still in the
// slot, so the two states disagree and it reads as the bake coming
// undone.
//
// Scoped to the renderer AND the slot that was actually repointed. The
// original material is usually worn by other meshes as well, and those
// have no bake of their own: handing them a plug's deform would bend
// the wrong mesh.
//
// Shared because the bug is not the converter's. The native toolkit
// replaces the same slot on the same kind of renderer, and an avatar
// set up natively is MORE likely to have its toggles already built.
#if CVR_CCK_EXISTS
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
        // clips to hand it. `skipped` names clips that matched but are not this tool's
        // to edit, so the caller can say so rather than leaving a plug that
        // half works.
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
                // NEVER a clip this tool does not own. A clip under Packages, or the CCK's,
                // is shared with every project that has it: editing in place
                // reaches the package file itself. The native path walks the
                // user's LIVE controllers, so this is not hypothetical the way it
                // was for the converter, whose clips are already its own clones.
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
        // generated override that is not what ships, and on an avatar never
        // built it is often empty. Reading only that slot means finding nothing
        // at all on an ordinary avatar, and doing nothing quietly.
        //
        // All three are read, because a clip only has to be REACHABLE to fire.
        // Unfiltered: the caller decides what it may write to, and one caller
        // wants to report the ones it must leave alone.
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


        // Every OTHER material an animation can put in this slot, baked as well.
        //
        // Follow above repairs the swap that hands the slot back to what the bake
        // replaced. It cannot help a second look: a skin variant, a glow version,
        // an alternate the author toggles in was never the bake source, so nothing
        // repoints it and the mesh wears a material with no deform for as long as
        // that toggle is on. Nothing reports it either, because the slot the tool
        // checks still holds the baked copy.
        //
        // The bake belongs to the MESH, not the material, so the same result goes
        // into each variant and only the look differs. `primary` is the slot's
        // finished material, every knob and flag already written: each variant
        // copies it and is remembered on the plug.
        public static int FollowVariants(YapsPlug plug, Renderer renderer, int slot, YapsBaker.Result result,
                                         string dir, IEnumerable<AnimationClip> clips,
                                         BridgeReport report, Material primary, params Material[] known)
        {
            if (renderer == null || clips == null) return 0;
            var runnable = clips.Where(c => c != null).Distinct().ToList();
            string path = AnimationUtility.CalculateTransformPath(renderer.transform,
                                                                  AnimationRootOf(renderer.transform));
            var baked = BakeVariants(renderer, slot, result, dir, runnable, report, primary, known);
            foreach (var (_, copy) in baked) Remember(plug, renderer, slot, copy);
            // A re-bake skips the variants it baked before, as already carrying a
            // bake, and one baked before the plug kept this record is on no list.
            // Any keyed here that wears this slot's bake texture is one of them,
            // and every one is brought level with the new values.
            var bake = primary != null && primary.HasProperty("_YAPS_Bake") ? primary.GetTexture("_YAPS_Bake") : null;
            if (bake != null)
            {
                foreach (var m in Keyed(runnable, path, slot))
                {
                    if (m != primary && m.HasProperty("_YAPS_Bake") && m.GetTexture("_YAPS_Bake") == bake)
                        Remember(plug, renderer, slot, m);
                }
            }
            Refresh(plug);
            if (baked.Count == 0) return 0;
            // Only a variant something now swaps in counts as followed. One
            // whose swap sits in a package clip is baked and never worn.
            int repointed = 0, followed = 0;
            var skipped = new SortedSet<string>();
            foreach (var (variant, copy) in baked)
            {
                int n = RepointInClips(runnable, path, slot, variant, copy, skipped);
                repointed += n;
                if (n > 0) followed++;
            }
            WarnSkipped(report, skipped);
            if (followed > 0 && report != null)
            {
                report.Converted("YAPS",
                    $"Baked {followed} alternate material(s) the animator swaps in",
                    "So the plug still bends while an alternate skin is on.");
            }
            return repointed;
        }

        // The bake half alone, each variant with its baked copy, for a caller
        // that keeps the pairs. The converter is one, and calls it only once
        // its clips are its own copies, or repointing edits the author's.
        public static List<(Material variant, Material baked)> BakeVariants(
            Renderer renderer, int slot, YapsBaker.Result result, string dir,
            IEnumerable<AnimationClip> clips, BridgeReport report, Material primary, params Material[] known)
        {
            var done = new List<(Material variant, Material baked)>();
            if (renderer == null || result == null || clips == null) return done;
            Transform root = AnimationRootOf(renderer.transform);
            if (root == null) return done;

            string path = AnimationUtility.CalculateTransformPath(renderer.transform, root);
            var skip = new HashSet<Material>(known.Append(primary).Where(m => m != null));
            // A material already carrying a bake is one of ours from an
            // earlier run. Baking a baked material buries the original.
            var variants = Keyed(clips, path, slot)
                .Where(m => !m.HasProperty("_YAPS_Bake") && !skip.Contains(m)).ToList();

            var refused = new SortedSet<string>();
            foreach (var variant in variants)
            {
                var source = variant;
                var legacy = YapsLegacyMap.Detect(source, out _);
                // DPS has no switch for its own deform, so its shader is never
                // patched; Simple Lit carries the look instead. Same rule the plug's
                // own material follows.
                var shader = legacy == YapsLegacyMap.Origin.DPS ? null
                    : YapsShaderPatcher.Patch(source, dir, report, out _, out _,
                                              allowSps: legacy == YapsLegacyMap.Origin.SPS);
                if (shader == null)
                {
                    var plain = YapsNativeBuilder.OnSimpleLit(source, out _);
                    shader = plain != null
                        ? YapsShaderPatcher.Patch(plain, dir, report, out _, out _)
                        : null;
                    if (shader != null) source = plain;
                }
                if (shader == null) { refused.Add(variant.name); continue; }

                var baked = YapsBaker.Apply(result, source, shader, dir, result.FromSkinnedMesh);
                if (legacy != YapsLegacyMap.Origin.None && legacy != YapsLegacyMap.Origin.YAPS)
                {
                    YapsNativeBuilder.SwitchOffLegacyDeform(baked, legacy);
                }
                // The bake alone left the variant on the shader's defaults: no
                // atlas, no tags, no own-socket ticks, stock curvature. Same mesh
                // and slot, so the primary's bake fields are this one's too.
                YapsNativeBuilder.CopyYapsProperties(primary, baked);
                done.Add((variant, baked));
            }

            if (refused.Count > 0 && report != null)
            {
                report.Warning("YAPS",
                    $"{refused.Count} swapped-in material(s) could not take the deform",
                    "Their shaders refused the deform, so the plug won't bend while they're on. Give them a " +
                    "shader with source and bake again: " + string.Join(", ", refused));
            }
            return done;
        }

        // Every material a clip keys into this path and slot, once each.
        static IEnumerable<Material> Keyed(IEnumerable<AnimationClip> clips, string path, int slot)
        {
            var seen = new HashSet<Material>();
            foreach (var clip in clips.Where(c => c != null).Distinct())
            {
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    if (binding.path != path || SlotIndex(binding.propertyName) != slot) continue;
                    var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                    if (keys == null) continue;
                    foreach (var key in keys)
                    {
                        var m = key.value as Material;
                        if (m != null && seen.Add(m)) yield return m;
                    }
                }
            }
        }

        // A baked variant onto the plug its slot belongs to, once.
        public static void Remember(YapsPlug plug, Renderer renderer, int slot, Material baked)
        {
            if (plug == null || renderer == null || baked == null) return;
            if (plug.variants.Any(v => v != null && v.material == baked && v.renderer == renderer && v.slot == slot)) return;
            plug.variants.Add(new YapsPlug.SlotVariant { renderer = renderer, slot = slot, material = baked });
            EditorUtility.SetDirty(plug);
        }

        // Every remembered variant brought level with what its slot wears now,
        // so a knob changed after the bake reaches the alternate look as well.
        public static void Refresh(YapsPlug plug)
        {
            if (plug == null) return;
            foreach (var v in plug.variants)
            {
                if (v == null || v.renderer == null || v.material == null) continue;
                var mats = v.renderer.sharedMaterials;
                if (v.slot < 0 || v.slot >= mats.Length) continue;
                var primary = mats[v.slot];
                // A slot Remove put back holds no bake to copy.
                if (primary == null || primary == v.material || !primary.HasProperty("_YAPS_Bake")) continue;
                YapsNativeBuilder.CopyYapsProperties(primary, v.material);
                EditorUtility.SetDirty(v.material);
            }
        }

        // The native path: no merged controller to read, so the clips come off
        // whatever the avatar or prop actually runs. All three sources, for the
        // reason given above RunnableClips: a swap that fires from the wrong
        // controller breaks the plug just the same. A caller following several
        // slots passes RunnableClips once, since walking every controller per
        // slot repeats the same answer.
        public static int Follow(Renderer renderer, int slot, Material from, Material to,
                                 BridgeReport report = null, IList<AnimationClip> clips = null)
        {
            if (renderer == null || from == null || to == null || from == to)
            {
                return 0;
            }

            Transform root = AnimationRootOf(renderer.transform);
            if (clips == null) clips = RunnableClips(renderer.transform);
            if (root == null || clips.Count == 0)
            {
                return 0;
            }

            string path = AnimationUtility.CalculateTransformPath(renderer.transform, root);
            var skipped = new SortedSet<string>();
            int repointed = RepointInClips(clips, path, slot, from, to, skipped);
            WarnSkipped(report, skipped);
            if (repointed > 0 && report != null)
            {
                report.Converted("YAPS",
                    $"Pointed {repointed} material swap(s) at the baked material",
                    "Otherwise they would swap the unbaked material back in and the plug would straighten.");
            }
            return repointed;
        }

        static void WarnSkipped(BridgeReport report, ICollection<string> skipped)
        {
            if (skipped.Count == 0 || report == null) return;
            report.Warning("YAPS",
                $"{skipped.Count} material swap(s) could not be repointed",
                "They live in a package, so they were not edited, and playing one straightens the plug. Copy " +
                "the clip into your project: " + string.Join(", ", skipped));
        }
    }
}
#endif
