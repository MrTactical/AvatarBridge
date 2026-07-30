using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    /// <summary>
    /// Persists an in-memory AnimatorController (built by AnimatorDeepCopier / the merger)
    /// as an asset. Every sub-object created with "new" must be added to the asset file or
    /// Unity silently drops it on reload.
    /// </summary>
    public static class AnimatorAssetSaver
    {
        /// <summary>
        /// Writes the controller to <paramref name="assetPath"/> and returns the persisted
        /// asset — which is NOT always the object passed in, so callers must use what comes
        /// back when wiring up components.
        ///
        /// Converting the same avatar twice used to break the first result. Deleting the file
        /// takes its .meta with it, and .meta is where the GUID lives, so the rebuilt
        /// controller came back with a fresh one and every existing reference — an earlier
        /// converted copy still in the scene, a prefab, an override controller — silently
        /// became "Missing (Runtime Animator Controller)". So when something is already at the
        /// path, the new controller is built alongside it and only its bytes are copied over,
        /// leaving the .meta (and the GUID everything resolves through) untouched.
        /// </summary>
        public static AnimatorController Save(AnimatorController controller, string assetPath)
        {
            return Persist(controller, assetPath, buildPath =>
            {
                var seen = new HashSet<Object>();
                foreach (var layer in controller.layers)
                {
                    // Generated masks (hand/muscle replacements) live in memory until now.
                    Add(layer.avatarMask, controller, seen);
                    AddMachine(layer.stateMachine, controller, seen);
                }
                AssetDatabase.SaveAssets();
                ValidateSavedController(controller, buildPath);
            });
        }

        /// <summary>
        /// Same GUID-stable write for the override controller. ChilloutVR runs the avatar off
        /// the override, so a changed GUID there breaks the avatar itself, not just references
        /// to it.
        /// </summary>
        public static AnimatorOverrideController SaveOverride(AnimatorOverrideController overrides, string assetPath)
        {
            return Persist(overrides, assetPath, null);
        }

        /// <summary>
        /// Creates the asset, lets <paramref name="populate"/> attach any sub-objects, and
        /// returns whatever ends up living at <paramref name="assetPath"/>. When a previous
        /// build is already there, the new asset is written beside it and only its bytes are
        /// copied across, so the original .meta survives and the GUID never changes.
        /// </summary>
        static T Persist<T>(T asset, string assetPath, System.Action<string> populate) where T : Object
        {
            bool replacing = !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath));
            string scratchDir = replacing ? ScratchDirFor(assetPath) : null;
            string buildPath = assetPath;

            if (replacing)
            {
                // A scratch FOLDER, keeping the real file name — never a scratch file name.
                // CreateAsset renames the object after the file it writes, so building as
                // "__AvatarBridge_rebuild.controller" bakes that name into the asset and into
                // everything derived from it: the override controller took its own name from
                // the controller's and landed at the wrong path entirely.
                Directory.CreateDirectory(AbsolutePath(scratchDir));
                AssetDatabase.Refresh();
                buildPath = scratchDir + "/" + Path.GetFileName(assetPath);
            }

            FileUtil.DeleteFileOrDirectory(buildPath);
            AssetDatabase.Refresh();
            AssetDatabase.CreateAsset(asset, buildPath);
            populate?.Invoke(buildPath);
            AssetDatabase.SaveAssets();

            if (!replacing)
            {
                return asset;
            }

            File.Copy(AbsolutePath(buildPath), AbsolutePath(assetPath), true);
            AssetDatabase.DeleteAsset(scratchDir);
            // ForceSynchronousImport as well as ForceUpdate: without it the reimport can still be
            // in flight when this returns, and the caller hands a half-imported controller to a
            // live Animator. Unity then builds a Mecanim graph from data that isn't there and
            // segfaults — ten crash dumps in one morning, every one inside GenerateGraph or the
            // player loop that ran the graph it produced.
            AssetDatabase.ImportAsset(assetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            // The object we built belonged to the scratch asset just deleted; the live one is
            // whatever was reimported at the original path.
            var persisted = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (persisted == null)
            {
                Debug.LogError($"[AvatarBridge] Rebuilt asset could not be reloaded from {assetPath}!");
                return asset;
            }
            return persisted;
        }

        static string ScratchDirFor(string assetPath)
        {
            return Path.GetDirectoryName(assetPath).Replace('\\', '/') + "/__AvatarBridgeRebuild";
        }

        static string AbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        /// <summary>
        /// Guards against silent data loss: if any state failed to persist (a missed
        /// AddObjectToAsset), the reloaded controller shrinks and layers break in-game
        /// with no visible error. Compare counts and complain loudly.
        /// </summary>
        static void ValidateSavedController(AnimatorController original, string assetPath)
        {
            var reloaded = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (reloaded == null)
            {
                Debug.LogError($"[AvatarBridge] Saved controller could not be reloaded from {assetPath}!");
                return;
            }
            int originalLayers = original.layers.Length;
            int reloadedLayers = reloaded.layers.Length;
            int originalStates = CountStates(original);
            int reloadedStates = CountStates(reloaded);
            if (originalLayers != reloadedLayers || originalStates != reloadedStates)
            {
                Debug.LogError($"[AvatarBridge] Controller lost data on save: layers {originalLayers}->{reloadedLayers}, " +
                               $"states {originalStates}->{reloadedStates}. Report this at the AvatarBridge repo.");
            }
            else
            {
                Debug.Log($"[AvatarBridge] Controller saved intact: {reloadedLayers} layers, {reloadedStates} states.");
            }
        }

        static int CountStates(AnimatorController controller)
        {
            int count = 0;
            void Walk(AnimatorStateMachine machine)
            {
                if (machine == null)
                {
                    return;
                }
                count += machine.states.Length;
                foreach (var child in machine.stateMachines)
                {
                    Walk(child.stateMachine);
                }
            }
            foreach (var layer in controller.layers)
            {
                Walk(layer.stateMachine);
            }
            return count;
        }

        static void Add(Object obj, AnimatorController asset, HashSet<Object> seen)
        {
            if (obj == null || seen.Contains(obj))
            {
                return;
            }
            seen.Add(obj);
            // Anything already stored in some asset (CCK clips, VRC clips, shared masks)
            // must stay where it is; only orphaned in-memory objects get embedded.
            if (!AssetDatabase.Contains(obj))
            {
                obj.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(obj, asset);
            }
        }

        static void AddMachine(AnimatorStateMachine machine, AnimatorController asset, HashSet<Object> seen)
        {
            if (machine == null)
            {
                return;
            }
            Add(machine, asset, seen);

            foreach (var behaviour in machine.behaviours)
            {
                Add(behaviour, asset, seen);
            }
            foreach (var transition in machine.anyStateTransitions)
            {
                Add(transition, asset, seen);
            }
            foreach (var transition in machine.entryTransitions)
            {
                Add(transition, asset, seen);
            }

            foreach (var child in machine.states)
            {
                Add(child.state, asset, seen);
                foreach (var behaviour in child.state.behaviours)
                {
                    Add(behaviour, asset, seen);
                }
                foreach (var transition in child.state.transitions)
                {
                    Add(transition, asset, seen);
                }
                AddMotion(child.state.motion, asset, seen);
            }

            foreach (var child in machine.stateMachines)
            {
                foreach (var transition in machine.GetStateMachineTransitions(child.stateMachine))
                {
                    Add(transition, asset, seen);
                }
                AddMachine(child.stateMachine, asset, seen);
            }
        }

        static void AddMotion(Motion motion, AnimatorController asset, HashSet<Object> seen)
        {
            if (motion is BlendTree tree)
            {
                Add(tree, asset, seen);
                foreach (var child in tree.children)
                {
                    AddMotion(child.motion, asset, seen);
                }
            }
            else if (motion is AnimationClip clip)
            {
                // Generated clips (e.g. renamed-parameter clones) need persisting too.
                Add(clip, asset, seen);
            }
        }
    }
}
