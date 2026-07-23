using System.Collections.Generic;
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
        public static void Save(AnimatorController controller, string assetPath)
        {
            FileUtil.DeleteFileOrDirectory(assetPath);
            AssetDatabase.Refresh();
            AssetDatabase.CreateAsset(controller, assetPath);

            var seen = new HashSet<Object>();
            foreach (var layer in controller.layers)
            {
                // Generated masks (hand/muscle replacements) live in memory until now.
                Add(layer.avatarMask, controller, seen);
                AddMachine(layer.stateMachine, controller, seen);
            }

            AssetDatabase.SaveAssets();
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
