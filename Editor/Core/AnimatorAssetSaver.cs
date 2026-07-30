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
    ///
    /// REPLACING AN EXISTING BUILD MUTATES IT — it never deletes, byte-copies or reimports.
    ///
    /// This file used to keep the GUID stable the other way: build the new controller in a
    /// scratch folder, File.Copy its bytes over the old asset, force a synchronous reimport.
    /// The GUID survived; the OBJECT did not. A reimport destroys the native controller and
    /// every sub-asset and creates replacements, so everything still holding the old ones —
    /// the Animator window ("AnimatorStateMachine has been destroyed" spam), tester tools,
    /// anything with domain reload switched off — was left holding corpses. With Unity's
    /// "Enter Play Mode Options" on, nothing between conversions throws that stale state
    /// away, and pressing Play re-awakes Animators against it: a session-long crash series,
    /// SIGSEGV inside GenerateGraph/DoBlendTreeEvaluation, preceded by
    /// "Assertion failed: 'MecanimDataWasBuilt()'" — a graph built from controller data that
    /// was never rebuilt. Hand-edited controllers never crash like this, because hand-editing
    /// MUTATES the existing object. Now so does this: same GUID because it is the same FILE,
    /// same native object because nothing ever destroys it, and Unity rebuilds its mecanim
    /// data the ordinary lazy way it does for any edited controller.
    /// </summary>
    public static class AnimatorAssetSaver
    {
        /// <summary>
        /// Writes the controller to <paramref name="assetPath"/> and returns the persisted
        /// asset — which is NOT always the object passed in (replacing an earlier build
        /// returns that build's object, refilled), so callers must use what comes back when
        /// wiring up components.
        /// </summary>
        public static AnimatorController Save(AnimatorController controller, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(controller, assetPath);
                Embed(controller, controller);
                AssetDatabase.SaveAssets();
                ValidateSavedController(controller, assetPath);
                return controller;
            }

            // Empty the old asset: every sub-object is about to be superseded, and leaving
            // them would accumulate one dead copy of everything per reconversion. The ROOT is
            // deliberately never touched — being the same object is the whole point.
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (sub != null && sub != existing)
                {
                    AssetDatabase.RemoveObjectFromAsset(sub);
                    Object.DestroyImmediate(sub, true);
                }
            }

            // The root's serialized data — name, parameters, the layer list pointing at the
            // new in-memory state machines — copied member-for-member onto the live object.
            EditorUtility.CopySerialized(controller, existing);
            Embed(existing, existing);
            AssetDatabase.SaveAssets();
            ValidateSavedController(existing, assetPath);
            return existing;
        }

        /// <summary>
        /// Same mutate-in-place write for the override controller. ChilloutVR runs the avatar
        /// off the override, so a changed GUID there breaks the avatar itself, not just
        /// references to it — and a destroyed-and-recreated object is as stale as one.
        /// </summary>
        public static AnimatorOverrideController SaveOverride(AnimatorOverrideController overrides, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(assetPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(overrides, assetPath);
                AssetDatabase.SaveAssets();
                return overrides;
            }
            EditorUtility.CopySerialized(overrides, existing);
            AssetDatabase.SaveAssets();
            return existing;
        }

        /// <summary>Embeds every reachable in-memory sub-object into the asset.</summary>
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
