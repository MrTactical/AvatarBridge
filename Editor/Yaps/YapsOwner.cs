// The wearer's owner id, onto every renderer whose material can hold it, so
// a plug can tell its own avatar's sockets from anybody else's at any
// distance. The atlas carries the socket's copy; see yaps_atlas.cginc.
//
// ChilloutVR's parameter stream hands over a CRC32 of the wearer's user id,
// but only on the wearer's own copy: the stream is a local component and is
// destroyed on everybody else's. So the parameter SYNCS, and a remote copy
// reads zero until its first sync, which the shaders take as unknown and
// answer with the geometric guess they used before.
#if CVR_CCK_EXISTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsOwner
    {
        public const string Parameter = "YAPS/Owner";
        const string LayerName = "YAPS owner";
        const string Property = "_YAPS_Owner";

        // The client's enum numbers, written as numbers: a CCK whose enum
        // predates a name still serialises what the client reads.
        const int SeedOwner = 90000, AvatarAnimator = 2, Mod = 141, AvatarReference = 1;

        // The client's Mod works on the id as an integer, and a float holds
        // every integer below 2^24, so this keeps 24 bits of it whole.
        const float Modulus = 16777216f;

        // Idempotent, like the lighthouse: run it after any plug or socket
        // change. With nothing left carrying the property it takes itself out.
        public static string Wire(GameObject root, AnimatorController controller)
        {
            if (root == null || controller == null) return null;
            var targets = root.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.sharedMaterials.Any(m => m != null && m.HasProperty(Property)))
                .ToList();
            bool had = RemoveLayer(controller);
            if (targets.Count == 0)
            {
                had |= RemoveParameter(controller);
                had |= Unstream(root);
                return had ? "owner id removed: nothing left carries it" : null;
            }
            if (!Stream(root))
            {
                return "owner id not wired: this CCK's parameter stream has no entry list";
            }
            if (!controller.parameters.Any(p => p.name == Parameter))
            {
                controller.AddParameter(Parameter, AnimatorControllerParameterType.Float);
            }
            AddLayer(root.transform, controller, targets);
            return $"owner id on {targets.Count} renderer(s), from the parameter stream, synced (32 bits)";
        }

        // Into the controller ChilloutVR uploads, picked as the native
        // channel picks it: the overrides, else the base, else the Animator's.
        public static string Wire(CVRAvatar avatar)
        {
            if (avatar == null) return null;
            var shipped = avatar.overrides != null ? avatar.overrides.runtimeAnimatorController : null;
            if (shipped == null && avatar.avatarSettings != null) shipped = avatar.avatarSettings.baseController;
            var animator = avatar.GetComponent<Animator>();
            if (shipped == null && animator != null) shipped = animator.runtimeAnimatorController;
            return Wire(avatar.gameObject, BridgeContext.Underlying(shipped));
        }

        // A direct blend tree weighted by the parameter itself, over a clip
        // setting the property to 1, so the property comes out AS the id.
        // Write defaults on, over a default of zero, so the tree is a plain
        // sum and nothing of last frame's value is left in it.
        static void AddLayer(Transform root, AnimatorController controller, List<Renderer> targets)
        {
            var clip = new AnimationClip { name = LayerName };
            foreach (var r in targets)
            {
                clip.SetCurve(AnimationUtility.CalculateTransformPath(r.transform, root), r.GetType(),
                    "material." + Property, AnimationCurve.Constant(0f, 1f / 60f, 1f));
            }
            var tree = new BlendTree
            {
                name = LayerName,
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy,
                children = new[] { new ChildMotion { motion = clip, timeScale = 1f, directBlendParameter = Parameter } },
            };
            var machine = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
            // Built after the controller became an asset, so the parts embed
            // themselves or serialise as nothing.
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(controller)))
            {
                AssetDatabase.AddObjectToAsset(machine, controller);
                AssetDatabase.AddObjectToAsset(tree, controller);
                AssetDatabase.AddObjectToAsset(clip, controller);
            }
            var state = machine.AddState("Owner");
            state.motion = tree;
            state.writeDefaultValues = true;
            machine.defaultState = state;

            var layers = controller.layers.ToList();
            layers.Add(new AnimatorControllerLayer { name = LayerName, defaultWeight = 1f, stateMachine = machine });
            controller.layers = layers.ToArray();
            EditorUtility.SetDirty(controller);
        }

        // referenceType by hand. The CCK's inspector sets it only when it is
        // opened, so a stream added from code stays World, and the client
        // computes SeedOwner for an Avatar reference alone.
        static bool Stream(GameObject root)
        {
            var stream = root.GetComponent<CVRParameterStream>();
            if (stream == null) stream = root.AddComponent<CVRParameterStream>();
            var entries = Entries(stream);
            var entryType = entries?.GetType().GetGenericArguments().FirstOrDefault();
            if (entryType == null) return false;

            Unstream(entries);
            var entry = Activator.CreateInstance(entryType);
            Set(entry, "type", SeedOwner);
            Set(entry, "targetType", AvatarAnimator);
            Set(entry, "applicationType", Mod);
            Set(entry, "staticValue", Modulus);
            Set(entry, "parameterName", Parameter);
            entries.Add(entry);
            Set(stream, "referenceType", AvatarReference);
            EditorUtility.SetDirty(stream);
            return true;
        }

        static bool Unstream(GameObject root)
        {
            var stream = root.GetComponent<CVRParameterStream>();
            var entries = stream != null ? Entries(stream) : null;
            if (entries == null || !Unstream(entries)) return false;
            if (entries.Count == 0) Undo.DestroyObjectImmediate(stream);
            else EditorUtility.SetDirty(stream);
            return true;
        }

        static bool Unstream(IList entries)
        {
            bool removed = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                var name = e?.GetType().GetField("parameterName")?.GetValue(e) as string;
                if (name != Parameter) continue;
                entries.RemoveAt(i);
                removed = true;
            }
            return removed;
        }

        static IList Entries(CVRParameterStream stream)
        {
            var field = stream.GetType().GetField("entries", BindingFlags.Public | BindingFlags.Instance);
            if (field == null) return null;
            if (!(field.GetValue(stream) is IList list))
            {
                list = (IList)Activator.CreateInstance(field.FieldType);
                field.SetValue(stream, list);
            }
            return list;
        }

        static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null) return;
            field.SetValue(target, field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, value) : value);
        }

        static bool RemoveLayer(AnimatorController controller)
        {
            bool removed = false;
            for (int i = controller.layers.Length - 1; i >= 0; i--)
            {
                if (controller.layers[i].name != LayerName) continue;
                controller.RemoveLayer(i);
                removed = true;
            }
            return removed;
        }

        static bool RemoveParameter(AnimatorController controller)
        {
            var p = controller.parameters.FirstOrDefault(x => x.name == Parameter);
            if (p == null) return false;
            controller.RemoveParameter(p);
            return true;
        }
    }
}
#endif
