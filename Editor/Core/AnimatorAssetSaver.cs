using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    // Saves an in-memory controller. Every sub-object made with "new" must be added to the file,
    // or Unity silently drops it on reload.
    //
    // REPLACING AN EXISTING BUILD MUTATES IT; never delete, byte-copy or reimport. A reimport
    // destroys the native controller and its sub-assets, leaving open windows and tools holding
    // dead objects; with Enter Play Mode Options on, Play then crashes (SIGSEGV in
    // GenerateGraph/DoBlendTreeEvaluation after "MecanimDataWasBuilt()" asserts). Mutating keeps
    // the same object, as hand-editing does.
    public static class AnimatorAssetSaver
    {
        public static AnimatorController Save(AnimatorController controller, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(controller, assetPath);
                Embed(controller, controller);
                AssetDatabase.SaveAssets();
                return controller;
            }

            // Empty the old asset, or each reconversion leaves a dead copy of everything. The
            // root is never touched: staying the same object is the point.
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (sub != null && sub != existing)
                {
                    AssetDatabase.RemoveObjectFromAsset(sub);
                    Object.DestroyImmediate(sub, true);
                }
            }

            // The root's serialized data; name, parameters, the layer list pointing at the
            // new in-memory state machines; copied member-for-member onto the live object.
            EditorUtility.CopySerialized(controller, existing);
            Embed(existing, existing);
            AssetDatabase.SaveAssets();
            // Its data now lives on the existing root, and nothing holds it.
            // Only the root object goes; the machines it listed are the asset's now.
            if (controller != existing && !AssetDatabase.Contains(controller))
            {
                Object.DestroyImmediate(controller);
            }
            return existing;
        }

        public static AnimatorOverrideController SaveOverride(AnimatorOverrideController overrides, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(assetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(overrides, assetPath);
                AssetDatabase.SaveAssets();
                return Imported(assetPath, overrides);
            }
            EditorUtility.CopySerialized(overrides, existing);
            AssetDatabase.SaveAssets();
            return Imported(assetPath, existing);
        }

        static AnimatorOverrideController Imported(string assetPath, AnimatorOverrideController fallback)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(assetPath) ?? fallback;
        }

        static void Embed(AnimatorController host, AnimatorController asset)
        {
            var seen = new HashSet<Object>();
            foreach (var layer in host.layers)
            {
                // Generated masks (hand/muscle replacements) live in memory until now.
                Add(layer.avatarMask, asset, seen);
                AddMachine(layer.stateMachine, asset, seen);
            }
        }

        // For a layer added after Save ran: unembedded, it serializes with a null state machine,
        // driving nothing, silently.
        internal static void EmbedLayer(AnimatorControllerLayer layer, AnimatorController asset)
        {
            if (layer == null || asset == null
                || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)))
            {
                return;
            }
            var seen = new HashSet<Object>();
            Add(layer.avatarMask, asset, seen);
            AddMachine(layer.stateMachine, asset, seen);
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
