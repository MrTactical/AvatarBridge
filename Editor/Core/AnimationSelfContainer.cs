#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.IO;
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
    // the output folder (ours, travels with the conversion).
    public static class AnimationSelfContainer
    {
        const string Category = "Animator";

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
            EnsureFolder(dir);

            var map = new Dictionary<Motion, Motion>();
            var maskMap = new Dictionary<AvatarMask, AvatarMask>();
            var copiedClips = new List<string>();
            int copiedMasks = 0;

            var layers = controller.layers;
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

            if (copiedClips.Count > 0 || copiedMasks > 0)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                copiedClips.Sort(StringComparer.Ordinal);
                ctx.Report.Converted(Category,
                    $"Controller made self-contained — {copiedClips.Count} clip(s), {copiedMasks} mask(s) copied into RehomedAssets",
                    "Every animation the avatar plays now ships inside the output folder. The controller used to " +
                    "reference clips wherever the source avatar kept them; in a project without those folders each " +
                    "missing clip silently plays as stillness — frozen fingers, dead emotes — with no error anywhere. " +
                    "The CCK's own clips are the one exclusion: uploading requires the CCK installed, so those are " +
                    "always present. Copied: " + string.Join(", ", copiedClips) + ".");
            }
            else
            {
                ctx.Report.Converted(Category, "Controller already self-contained",
                    "Every clip and mask it references lives in the output folder, inside the controller itself, " +
                    "or ships with the CCK.");
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
                string dst = OutputAssetPaths.Claim($"{dir}/{SafeName(clip.name)}.anim");
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
                // reach into somebody else's controller. Clone it into ours first.
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
            string dst = OutputAssetPaths.Claim($"{dir}/{SafeName(mask.name)}.mask");
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
                return false; // embedded in the controller — already travels with it
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

        static void EnsureFolder(string dir)
        {
            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", dir));
            Directory.CreateDirectory(abs);
            AssetDatabase.Refresh();
        }

        static string SafeName(string n)
        {
            if (string.IsNullOrEmpty(n))
            {
                return "Asset";
            }
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                n = n.Replace(c, '_');
            }
            return n;
        }
    }
}
#endif
