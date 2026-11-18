// Makes material-swap animations follow the bake. A clip that assigns the slot's
// original material hands back one with no deform, so the plug straightens in Play
// Mode while edit mode still shows the bake: it reads as the bake coming undone.
// Scoped to the repointed renderer AND slot: other meshes wearing the original have
// no bake. Shared by the converter and the native toolkit.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Globalization;
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

        // `skipped` names matched clips this tool may not edit, so the caller can say so.
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
                // Never a package or CCK clip: editing in place reaches the package file.
                // The native path walks live controllers, so this happens.
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
                    var ledger = Ledger.Of(clip);
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (keys[i].value != from)
                        {
                            continue;
                        }
                        if (!ours)
                        {
                            // Reported, not silent: it breaks the plug on that toggle.
                            skipped?.Add(clip.name);
                            break;
                        }
                        keys[i].value = to;
                        touched = true;
                        repointed++;
                        // Off a user's material: remembered. Back onto one: forgotten. Copy to
                        // copy keeps the first, which is the one to go back to.
                        if (!Generated(from)) ledger?.Set(binding, keys[i].time, from);
                        else if (!Generated(to) && EditorUtility.IsPersistent(to)) ledger?.Forget(binding, keys[i].time);
                    }
                    if (touched)
                    {
                        // Remove promises one undo step, and clip assets record nothing on their own.
                        Undo.RegisterCompleteObjectUndo(clip, "YAPS material swap");
                        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
                        EditorUtility.SetDirty(clip);
                        ledger?.Save();
                    }
                }
            }
            return repointed;
        }

        // Remembers each key's original material in the clip's importer, so a key whose copy
        // was deleted by hand can go back instead of drawing pink in game.
        sealed class Ledger
        {
            const string Head = "yaps-swap-originals\n";
            readonly AssetImporter importer;
            readonly string clipId;
            readonly List<string[]> lines;

            Ledger(AssetImporter importer, string clipId)
            {
                this.importer = importer;
                this.clipId = clipId;
                string data = importer.userData ?? "";
                lines = data.StartsWith(Head)
                    ? data.Substring(Head.Length).Split('\n').Select(l => l.Split('\t')).Where(f => f.Length == 5).ToList()
                    : new List<string[]>();
            }

            // Null for a clip with no file, or whose importer already carries somebody else's data.
            public static Ledger Of(AnimationClip clip)
            {
                if (clip == null || !EditorUtility.IsPersistent(clip)) return null;
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
                if (importer == null || !(string.IsNullOrEmpty(importer.userData) || importer.userData.StartsWith(Head))) return null;
                // Several clips can share one file, a controller's sub-assets, so each line names its own.
                return new Ledger(importer, GlobalObjectId.GetGlobalObjectIdSlow(clip).targetObjectId.ToString());
            }

            static string Time(float t) => t.ToString("R", CultureInfo.InvariantCulture);

            bool Is(string[] f, EditorCurveBinding b, float t) => f[0] == clipId && f[1] == b.path && f[2] == b.propertyName && f[3] == Time(t);

            public IEnumerable<(string path, string property, float time, string original)> Entries =>
                lines.Where(f => f[0] == clipId).Select(f => (f[1], f[2], float.Parse(f[3], CultureInfo.InvariantCulture), f[4]));

            public void Set(EditorCurveBinding b, float t, Material original)
            {
                Forget(b, t);
                if (EditorUtility.IsPersistent(original))
                    lines.Add(new[] { clipId, b.path, b.propertyName, Time(t), GlobalObjectId.GetGlobalObjectIdSlow(original).ToString() });
            }

            public void Forget(EditorCurveBinding b, float t) => lines.RemoveAll(f => Is(f, b, t));

            // Written by the clip's own save, which writes the importer too.
            public void Save()
            {
                string data = lines.Count == 0 ? "" : Head + string.Join("\n", lines.Select(f => string.Join("\t", f)));
                if (importer.userData != data) importer.userData = data;
            }
        }

        // A material YAPS wrote: its folders are all called YAPS, the toolkit's and a conversion's,
        // and a squash copy says so in its importer wherever it was put.
        static bool Generated(Material m)
        {
            string path = m != null ? AssetDatabase.GetAssetPath(m) : null;
            if (string.IsNullOrEmpty(path)) return false;
            return path.Replace('\\', '/').Split('/').Reverse().Skip(1).Contains("YAPS")
                   || (AssetImporter.GetAtPath(path)?.userData ?? "").StartsWith(YapsSquash.Mark);
        }

        // Remembered keys whose material is gone: the copy was deleted by hand.
        public static int MissingKeys(IEnumerable<AnimationClip> clips) => Walk(clips, false);

        // Puts each of those back on the material it held before YAPS, and forgets keys that went
        // back some other way. Keys still on a live copy are left to that copy's own Remove.
        public static int RestoreMissing(IEnumerable<AnimationClip> clips) => Walk(clips, true);

        static int Walk(IEnumerable<AnimationClip> clips, bool restore)
        {
            int count = 0;
            foreach (var clip in (clips ?? Enumerable.Empty<AnimationClip>()).Where(c => c != null).Distinct())
            {
                var ledger = Ledger.Of(clip);
                if (ledger == null) continue;
                var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                bool changed = false;
                foreach (var (path, property, time, original) in ledger.Entries.ToList())
                {
                    int at = Array.FindIndex(bindings, b => b.path == path && b.propertyName == property);
                    var keys = at >= 0 ? AnimationUtility.GetObjectReferenceCurve(clip, bindings[at]) : null;
                    int i = keys == null ? -1 : Array.FindIndex(keys, k => k.time == time);
                    if (i < 0 || (keys[i].value != null && !Generated(keys[i].value as Material)))
                    {
                        // The key is gone, or holds a user's material again.
                        if (restore && at >= 0) { ledger.Forget(bindings[at], time); changed = true; }
                        continue;
                    }
                    if (keys[i].value != null) continue;
                    var back = GlobalObjectId.TryParse(original, out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Material : null;
                    if (back == null) continue;
                    count++;
                    if (!restore) continue;
                    Undo.RegisterCompleteObjectUndo(clip, "YAPS material swap");
                    keys[i].value = back;
                    AnimationUtility.SetObjectReferenceCurve(clip, bindings[at], keys);
                    EditorUtility.SetDirty(clip);
                    ledger.Forget(bindings[at], time);
                    changed = true;
                }
                if (changed) ledger.Save();
            }
            return count;
        }

        // What animation paths are relative to.
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

        // Every clip the avatar could run. CVR ships avatar.overrides, else the base
        // controller; the Animator's slot is often empty. All three are read, as a
        // reachable clip fires. Unfiltered: callers decide what they may write.
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


        // Bakes every OTHER material an animation can put in this slot (skin variants
        // and the like), which Follow cannot repoint. The bake belongs to the mesh, so
        // each variant copies `primary`, the slot's finished material.
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
            // Variants baked earlier are skipped above; find them by bake texture so
            // they get the new values too.
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
            // Followed only if something swaps it in (not a package clip).
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
                    $"Baked {BridgeElements.Count(followed, "alternate material", "alternate materials")} the animator swaps in",
                    "So the plug still bends while an alternate skin is on.");
            }
            return repointed;
        }

        // The bake half alone. The converter calls it only once its clips are its own copies.
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
            // Already baked: ours from an earlier run; rebaking buries the original.
            var variants = Keyed(clips, path, slot)
                .Where(m => !m.HasProperty("_YAPS_Bake") && !skip.Contains(m)).ToList();

            var refused = new SortedSet<string>();
            foreach (var variant in variants)
            {
                var source = variant;
                var legacy = YapsLegacyMap.Detect(source, out _);
                // DPS without a deform switch is never patched; a fallback carries the
                // look, as for the plug's own material.
                var shader = legacy == YapsLegacyMap.Origin.DPS && !YapsNativeBuilder.HasDpsSwitch(source) ? null
                    : YapsShaderPatcher.Patch(source, report, out _, out _,
                                              allowSps: legacy == YapsLegacyMap.Origin.SPS,
                                              roles: YapsShaderPatcher.Roles.Plug);
                if (shader == null)
                {
                    var plain = YapsNativeBuilder.OnFallback(source, out _);
                    shader = plain != null
                        ? YapsShaderPatcher.Patch(plain, report, out _, out _, roles: YapsShaderPatcher.Roles.Plug)
                        : null;
                    if (shader != null) source = plain;
                }
                if (shader == null) { refused.Add(variant.name); continue; }

                var baked = YapsBaker.Apply(result, source, shader, dir, result.FromSkinnedMesh);
                if (legacy != YapsLegacyMap.Origin.None && legacy != YapsLegacyMap.Origin.YAPS)
                {
                    YapsNativeBuilder.SwitchOffLegacyDeform(baked, legacy);
                }
                // The bake alone leaves shader defaults; same mesh and slot, so
                // the primary's fields apply.
                YapsNativeBuilder.CopyYapsProperties(primary, baked);
                done.Add((variant, baked));
            }

            if (refused.Count > 0 && report != null)
            {
                report.Warning("YAPS",
                    $"{BridgeElements.Count(refused.Count, "swapped-in material", "swapped-in materials")} could not take the deform",
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

        // So a knob changed after the bake reaches the variants too.
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

        // The native path: clips from RunnableClips. Callers following several slots
        // pass them in once.
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
                    $"Pointed {BridgeElements.Count(repointed, "material swap", "material swaps")} at the baked material",
                    "Otherwise they would swap the unbaked material back in and the plug would straighten.");
            }
            return repointed;
        }

        static void WarnSkipped(BridgeReport report, ICollection<string> skipped)
        {
            if (skipped.Count == 0 || report == null) return;
            report.Warning("YAPS",
                $"{BridgeElements.Count(skipped.Count, "material swap", "material swaps")} could not be repointed",
                "They live in a package, so they were not edited, and playing one straightens the plug. Copy " +
                "the clip into your project: " + string.Join(", ", skipped));
        }
    }
}
#endif
