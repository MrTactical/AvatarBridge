#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    // Makes the merged controller self-contained: every animation clip and avatar mask it
    // references is copied into the conversion's RehomedAssets folder and the controller is
    // repointed at the copies.
    //
    // Born from a tester's frozen fingers. A converted controller referenced its clips
    // wherever the source avatar kept them. 71 of one avatar's 116 clips lived in the
    // source's own folders, including every hand-pose clip. Move that conversion to a project
    // without those folders and each reference silently resolves to None: the state machine
    // still runs, transitions still fire, and every pose "plays" as stillness, with no error
    // anywhere. Gestures frozen in game while the same controller works on the author's PC is
    // exactly how that presents.
    //
    // Two deliberate exclusions: the CCK's own assets (uploading requires the CCK installed,
    // so they are guaranteed present in any project that can upload), and anything already in
    // the output folder (generated, travels with the conversion).
    public static class AnimationSelfContainer
    {
        const string Category = "Animator";

        // Log only: how often the walk dirtied the controller, and how many planned copies were
        // not byte copies (a CopyAsset refused inside the batch lands there too).
        static int repoints, materialized;

        public static void Run(BridgeContext ctx)
        {
            var controller = ctx.MergedController;
            if (controller == null)
            {
                return;
            }
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(controllerPath))
            {
                // Nothing on disk to keep consistent with; the saver ran before the
                // controller was persisted, which is an ordering bug worth hearing about.
                ctx.Report.Warning(Category, "Self-containment skipped",
                    "The merged controller isn't a saved asset yet, so clips were not copied.");
                return;
            }
            string dir = ctx.OutputDir.TrimEnd('/') + "/RehomedAssets";
            OutputAssetPaths.EnsureFolder(dir);

            var map = new Dictionary<Motion, Motion>();
            var maskMap = new Dictionary<AvatarMask, AvatarMask>();
            var copiedClips = new List<string>();
            int copiedMasks = 0;

            var layers = controller.layers;
            int made = CopyAll(layers, controller, controllerPath, ctx.OutputDir, dir, map, maskMap,
                copiedClips, ref copiedMasks, out int stale);
            repoints = 0;
            bool layersChanged = false;
            for (int i = 0; i < layers.Length; i++)
            {
                var mask = layers[i].avatarMask;
                if (mask != null && NeedsCopy(AssetDatabase.GetAssetPath(mask), ctx.OutputDir, controllerPath))
                {
                    layers[i].avatarMask = CopyMask(mask, dir, maskMap, ref copiedMasks);
                    layersChanged = true;
                }
                Walk(layers[i].stateMachine, controller, controllerPath, ctx.OutputDir, dir, map, copiedClips);
            }
            if (layersChanged)
            {
                controller.layers = layers;
            }
            Debug.Log($"[AvatarBridge] Self-contain: {made} copies made in one batch ({materialized} materialized), {stale} old files deleted in one call; " +
                      $"controller saves: {(made > 0 ? 1 : 0)} kept, up to {repoints} removed (one per repoint a copy used to follow)");

            if (copiedClips.Count > 0 || copiedMasks > 0)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                copiedClips.Sort(StringComparer.Ordinal);
                ctx.Report.Converted(Category,
                    $"Controller made self-contained: {BridgeElements.Count(copiedClips.Count, "clip", "clips")}, {BridgeElements.Count(copiedMasks, "mask", "masks")} copied into RehomedAssets",
                    "A missing clip plays as stillness with no error, so all but the CCK's own now ship in the " +
                    "output folder. Copied: " + string.Join(", ", copiedClips) + ".");
            }
            else
            {
                ctx.Report.Converted(Category, "Controller already self-contained",
                    "Every clip and mask it references lives in the output folder, inside the controller itself, " +
                    "or ships with the CCK.");
            }
        }

        // Every copy is made before anything is repointed. One at a time, each cost a delete
        // refresh, a copy refresh and a save of the controller the last repoint dirtied. Planned
        // in the order the walk meets them, so Claim hands out the same names, then copied in
        // one batch. A copy cannot be loaded inside it, so loading and editing wait for the Stop,
        // and the walk afterwards finds every copy already in the maps.
        static int CopyAll(AnimatorControllerLayer[] layers, AnimatorController controller, string controllerPath,
            string outputDir, string dir, Dictionary<Motion, Motion> map, Dictionary<AvatarMask, AvatarMask> maskMap,
            List<string> copied, ref int copiedMasks, out int staleCount)
        {
            var jobs = new List<(UnityEngine.Object source, string src, string dst)>();
            var stale = new List<string>();
            var seen = new HashSet<UnityEngine.Object>();
            materialized = 0;
            foreach (var layer in layers)
            {
                var mask = layer.avatarMask;
                if (mask != null && NeedsCopy(AssetDatabase.GetAssetPath(mask), outputDir, controllerPath) && seen.Add(mask))
                {
                    jobs.Add((mask, AssetDatabase.GetAssetPath(mask),
                        OutputAssetPaths.Claim($"{dir}/{OutputAssetPaths.SafeFileName(mask.name)}.mask", stale)));
                }
                PlanMachine(layer.stateMachine, controllerPath, outputDir, dir, seen, jobs, stale);
            }
            staleCount = stale.Count;
            if (jobs.Count == 0)
            {
                return 0;
            }
            // Outside the batch: CopyAsset refuses a destination that is still occupied.
            if (stale.Count > 0)
            {
                AssetDatabase.DeleteAssets(stale.ToArray(), new List<string>());
            }

            var made = new UnityEngine.Object[jobs.Count];
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < jobs.Count; i++)
                {
                    var (source, src, dst) = jobs[i];
                    // A standalone .anim or .mask copies byte-perfectly; one living inside an FBX
                    // or another controller has to be materialized into its own asset instead.
                    string extension = source is AvatarMask ? ".mask" : ".anim";
                    if (!(AssetDatabase.IsMainAsset(source)
                          && src.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                          && AssetDatabase.CopyAsset(src, dst)))
                    {
                        made[i] = Materialize(source, dst);
                        materialized++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            var clips = new List<AnimationClip>();
            for (int i = 0; i < jobs.Count; i++)
            {
                var (source, _, dst) = jobs[i];
                var copy = AssetDatabase.LoadAssetAtPath(dst, source.GetType()) ?? made[i];
                if (copy == null)
                {
                    copy = Materialize(source, dst); // copied, but the copy would not load
                }
                if (source is AvatarMask mask)
                {
                    maskMap[mask] = (AvatarMask)copy;
                    copiedMasks++;
                }
                else
                {
                    map[(Motion)source] = (Motion)copy;
                    copied.Add(source.name);
                    clips.Add((AnimationClip)copy);
                }
            }
            foreach (var clip in clips)
            {
                CarryAdditivePose(clip, controller, controllerPath, outputDir, dir, map, copied);
            }
            return jobs.Count;
        }

        static UnityEngine.Object Materialize(UnityEngine.Object source, string dst)
        {
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name;
            AssetDatabase.CreateAsset(copy, dst);
            return copy;
        }

        static void PlanMachine(AnimatorStateMachine sm, string controllerPath, string outputDir, string dir,
            HashSet<UnityEngine.Object> seen, List<(UnityEngine.Object, string, string)> jobs, List<string> stale)
        {
            if (sm == null)
            {
                return;
            }
            foreach (var child in sm.states)
            {
                if (child.state != null)
                {
                    Plan(child.state.motion, controllerPath, outputDir, dir, seen, jobs, stale);
                }
            }
            foreach (var child in sm.stateMachines)
            {
                PlanMachine(child.stateMachine, controllerPath, outputDir, dir, seen, jobs, stale);
            }
        }

        // Fix's order and dedupe, without its edits; a pose claims its name right after its clip,
        // as CarryAdditivePose does.
        static void Plan(Motion motion, string controllerPath, string outputDir, string dir,
            HashSet<UnityEngine.Object> seen, List<(UnityEngine.Object, string, string)> jobs, List<string> stale)
        {
            if (motion == null || !seen.Add(motion))
            {
                return;
            }
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                {
                    Plan(child.motion, controllerPath, outputDir, dir, seen, jobs, stale);
                }
                return;
            }
            if (!(motion is AnimationClip clip))
            {
                return;
            }
            string path = AssetDatabase.GetAssetPath(clip);
            bool copy = NeedsCopy(path, outputDir, controllerPath);
            if (copy)
            {
                jobs.Add((clip, path,
                    OutputAssetPaths.Claim($"{dir}/{OutputAssetPaths.SafeFileName(clip.name)}.anim", stale)));
            }
            else if (!Ours(path, outputDir, controllerPath))
            {
                return;
            }
            // A copy carries its source's settings, so the source answers for it.
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.hasAdditiveReferencePose && settings.additiveReferencePoseClip != null
                && NeedsCopy(AssetDatabase.GetAssetPath(settings.additiveReferencePoseClip), outputDir, controllerPath))
            {
                Plan(settings.additiveReferencePoseClip, controllerPath, outputDir, dir, seen, jobs, stale);
            }
        }

        static void Walk(AnimatorStateMachine sm, AnimatorController controller, string controllerPath,
            string outputDir, string dir, Dictionary<Motion, Motion> map, List<string> copied)
        {
            if (sm == null)
            {
                return;
            }
            foreach (var child in sm.states)
            {
                var state = child.state;
                if (state == null)
                {
                    continue;
                }
                var fixedMotion = Fix(state.motion, controller, controllerPath, outputDir, dir, map, copied);
                if (!ReferenceEquals(fixedMotion, state.motion))
                {
                    state.motion = fixedMotion;
                    EditorUtility.SetDirty(state);
                    repoints++;
                }
            }
            foreach (var child in sm.stateMachines)
            {
                Walk(child.stateMachine, controller, controllerPath, outputDir, dir, map, copied);
            }
        }

        static Motion Fix(Motion motion, AnimatorController controller, string controllerPath,
            string outputDir, string dir, Dictionary<Motion, Motion> map, List<string> copied)
        {
            if (motion == null)
            {
                return null;
            }
            if (map.TryGetValue(motion, out var existing))
            {
                return existing;
            }
            if (motion is AnimationClip clip)
            {
                string path = AssetDatabase.GetAssetPath(clip);
                if (!NeedsCopy(path, outputDir, controllerPath))
                {
                    // Not copied, but it may still carry a reference that has to travel.
                    map[clip] = clip;
                    CarryAdditivePose(clip, controller, controllerPath, outputDir, dir, map, copied);
                    return clip;
                }
                AnimationClip copy = null;
                string dst = OutputAssetPaths.Claim($"{dir}/{OutputAssetPaths.SafeFileName(clip.name)}.anim");
                // A standalone .anim copies byte-perfectly; a clip living inside an FBX or
                // another controller has to be materialized into its own asset instead.
                if (AssetDatabase.IsMainAsset(clip)
                    && path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)
                    && AssetDatabase.CopyAsset(path, dst))
                {
                    copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(dst);
                }
                if (copy == null)
                {
                    copy = UnityEngine.Object.Instantiate(clip);
                    copy.name = clip.name;
                    AssetDatabase.CreateAsset(copy, dst);
                }
                map[clip] = copy;
                copied.Add(clip.name);
                CarryAdditivePose(copy, controller, controllerPath, outputDir, dir, map, copied);
                return copy;
            }
            if (motion is BlendTree tree)
            {
                // A tree owned by some other asset can't be edited in place; that would
                // reach into somebody else's controller. Clone it first.
                string treePath = AssetDatabase.GetAssetPath(tree);
                var sourceTree = tree;
                bool ours = treePath == controllerPath;
                if (treePath != controllerPath && NeedsCopy(treePath, outputDir, controllerPath))
                {
                    ours = true;
                    var clone = UnityEngine.Object.Instantiate(tree);
                    clone.name = tree.name;
                    clone.hideFlags |= HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(clone, controller);
                    map[tree] = clone;
                    tree = clone;
                }
                else
                {
                    map[tree] = tree;
                }
                var children = tree.children;
                bool changed = false;
                for (int i = 0; i < children.Length; i++)
                {
                    var fixedMotion = Fix(children[i].motion, controller, controllerPath, outputDir, dir, map, copied);
                    if (!ReferenceEquals(fixedMotion, children[i].motion))
                    {
                        children[i].motion = fixedMotion;
                        changed = true;
                    }
                }
                if (changed)
                {
                    // NeedsCopy says no for a tree the CCK or an engine
                    // builtin owns, on the grounds that it travels with
                    // every project. That is true of the tree, and not of
                    // a CHILD this pass just copied: writing the new
                    // children back would edit the CCK's own asset, which
                    // is the shared-asset mistake this file exists to
                    // prevent and its comment above already names.
                    if (!ours)
                    {
                        var late = UnityEngine.Object.Instantiate(tree);
                        late.name = tree.name;
                        late.hideFlags |= HideFlags.HideInHierarchy;
                        AssetDatabase.AddObjectToAsset(late, controller);
                        map[sourceTree] = late;
                        tree = late;
                    }
                    tree.children = children;
                    EditorUtility.SetDirty(tree);
                    repoints++;
                }
                return tree;
            }
            map[motion] = motion;
            return motion;
        }

        static AvatarMask CopyMask(AvatarMask mask, string dir, Dictionary<AvatarMask, AvatarMask> map, ref int count)
        {
            if (map.TryGetValue(mask, out var existing))
            {
                return existing;
            }
            AvatarMask copy = null;
            string src = AssetDatabase.GetAssetPath(mask);
            string dst = OutputAssetPaths.Claim($"{dir}/{OutputAssetPaths.SafeFileName(mask.name)}.mask");
            if (AssetDatabase.IsMainAsset(mask)
                && src.EndsWith(".mask", StringComparison.OrdinalIgnoreCase)
                && AssetDatabase.CopyAsset(src, dst))
            {
                copy = AssetDatabase.LoadAssetAtPath<AvatarMask>(dst);
            }
            if (copy == null)
            {
                copy = UnityEngine.Object.Instantiate(mask);
                copy.name = mask.name;
                AssetDatabase.CreateAsset(copy, dst);
            }
            map[mask] = copy;
            count++;
            return copy;
        }

        // A clip's additive reference pose is a second clip and not a motion,
        // so the walk never reaches it. Runs for every clip this tool owns.
        static void CarryAdditivePose(AnimationClip clip, AnimatorController controller,
            string controllerPath, string outputDir, string dir,
            Dictionary<Motion, Motion> map, List<string> copied)
        {
            if (clip == null || !Ours(AssetDatabase.GetAssetPath(clip), outputDir, controllerPath))
            {
                return;   // somebody else's asset; editing it reaches their package
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            // Only when the pose is actually in use. The field is often left
            // populated on a clip that ignores it, and dragging a clip
            // nothing reads into the output folder is just litter.
            if (!settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip == null
                || !NeedsCopy(AssetDatabase.GetAssetPath(settings.additiveReferencePoseClip),
                              outputDir, controllerPath))
            {
                return;
            }

            settings.additiveReferencePoseClip = Fix(settings.additiveReferencePoseClip,
                controller, controllerPath, outputDir, dir, map, copied) as AnimationClip;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }

        // Editable here: in memory, embedded in the built controller, or in
        // this avatar's output folder. Never an engine or package asset.
        static bool Ours(string path, string outputDir, string controllerPath)
            => string.IsNullOrEmpty(path)
               || path == controllerPath
               || path.StartsWith(outputDir.TrimEnd('/') + "/", StringComparison.Ordinal);

        static bool NeedsCopy(string path, string outputDir, string controllerPath)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false; // in-memory: the bake-rescue passes own those
            }
            if (path == controllerPath)
            {
                return false; // embedded in the controller, already travels with it
            }
            if (path.StartsWith(outputDir.TrimEnd('/') + "/", StringComparison.Ordinal))
            {
                return false;
            }
            bool inProject = path.StartsWith("Assets/", StringComparison.Ordinal)
                          || path.StartsWith("Packages/", StringComparison.Ordinal);
            if (!inProject)
            {
                return false; // engine builtins live in every Unity install
            }
            if (path.Contains("ABI.CCK") || path.Contains("CVR.CCK"))
            {
                return false; // the CCK ships with every project that can upload
            }
            return true;
        }
    }
}
#endif
