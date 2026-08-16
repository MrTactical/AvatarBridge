// A vertex shader cannot read a blendshape weight or a bone's scale, and
// the plug's bake is its rest pose. So every clip that changes the plug's
// size writes the same change onto the plug's material as well: a shape
// curve onto its shape weight, the root bone's scale onto the bake scale.
// The converter runs this on its own clip copies; the toolkit on the
// avatar's own clips, and says so.
#if CVR_CCK_EXISTS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsCurveMirror
    {
        // Four float4s, sixteen shapes, matching SPS.
        public static string WeightProperty(int slot)
        {
            string pack = slot < 4 ? "_YAPS_ShapeWeights"
                        : slot < 8 ? "_YAPS_ShapeWeights2"
                        : slot < 12 ? "_YAPS_ShapeWeights3"
                        : "_YAPS_ShapeWeights4";
            return pack + "." + "xyzw"[slot & 3];
        }

        // Shape curves on the renderer become weight curves on its material.
        // Returns how many were written; `missed` names animated shapes that
        // move the plug but are not in the bake.
        public static int MirrorShapes(IEnumerable<AnimationClip> clips, string rendererPath, Type rendererType,
            IList<string> bakedShapes, ICollection<string> movingShapes, ISet<string> missed)
        {
            int written = 0;
            foreach (var clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.path != rendererPath
                        || !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string shape = binding.propertyName.Substring("blendShape.".Length);
                    int slot = bakedShapes.IndexOf(shape);
                    if (slot < 0)
                    {
                        if (movingShapes != null && movingShapes.Contains(shape))
                        {
                            missed?.Add(shape);
                        }
                        continue;
                    }
                    // Blendshapes animate 0..100; the bake stores full
                    // shapes, so the material takes 0..1.
                    var source = AnimationUtility.GetEditorCurve(clip, binding);
                    var scaled = new AnimationCurve();
                    foreach (var key in source.keys)
                    {
                        scaled.AddKey(new Keyframe(key.time, key.value * 0.01f,
                            key.inTangent * 0.01f, key.outTangent * 0.01f));
                    }
                    AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                    {
                        path = rendererPath,
                        type = rendererType,
                        propertyName = "material." + WeightProperty(slot),
                    }, scaled);
                    written++;
                }
            }
            return written;
        }

        // Which of a bone's local axes runs along the shaft: the bake's
        // forward, taken into the bone's space, and its dominant axis.
        // 0 x, 1 y, 2 z.
        public static int AlongAxis(Transform bone, Quaternion bakeRotation)
        {
            if (bone == null) return 2;
            var local = Quaternion.Inverse(bone.rotation) * (bakeRotation * Vector3.forward);
            float ax = Mathf.Abs(local.x), ay = Mathf.Abs(local.y), az = Mathf.Abs(local.z);
            return ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
        }

        // The chain root's scale curves become the bake scale (its length
        // axis) and the bake girth (a radial axis) on the material. One
        // bone per clip, the first that has a scale curve. Bones are given
        // as paths from the animator root.
        public static int MirrorBoneScale(IEnumerable<AnimationClip> clips, IEnumerable<string> bonePaths,
            string rendererPath, Type rendererType, int alongAxis)
        {
            var bones = new HashSet<string>(bonePaths.Where(p => p != null), StringComparer.Ordinal);
            if (bones.Count == 0)
            {
                return 0;
            }
            string[] axes = { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };
            string along = axes[Mathf.Clamp(alongAxis, 0, 2)];
            var radial = axes.Where(a => a != along).ToArray();

            int written = 0;
            foreach (var clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }
                // The bone this clip scales, if any of ours.
                string bone = null;
                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var b in bindings)
                {
                    if (b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal)
                        && bones.Contains(b.path))
                    {
                        bone = b.path;
                        break;
                    }
                }
                if (bone == null)
                {
                    continue;
                }
                AnimationCurve Curve(string property)
                {
                    foreach (var b in bindings)
                    {
                        if (b.type == typeof(Transform) && b.path == bone && b.propertyName == property)
                        {
                            return AnimationUtility.GetEditorCurve(clip, b);
                        }
                    }
                    return null;
                }
                var length = Curve(along);
                var girth = Curve(radial[0]) ?? Curve(radial[1]);
                bool any = false;
                if (length != null)
                {
                    AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                    {
                        path = rendererPath, type = rendererType, propertyName = "material._YAPS_BakeScale",
                    }, new AnimationCurve(length.keys));
                    any = true;
                }
                if (girth != null)
                {
                    AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                    {
                        path = rendererPath, type = rendererType, propertyName = "material._YAPS_BakeGirth",
                    }, new AnimationCurve(girth.keys));
                    any = true;
                }
                if (any)
                {
                    written++;
                }
            }
            return written;
        }

        // Every clip an animator plays, once each.
        public static IEnumerable<AnimationClip> ClipsOf(RuntimeAnimatorController controller)
        {
            if (controller == null)
            {
                yield break;
            }
            var seen = new HashSet<AnimationClip>();
            foreach (var clip in controller.animationClips)
            {
                if (clip != null && seen.Add(clip))
                {
                    yield return clip;
                }
            }
        }

        // A clip the toolkit may write into: the user's own, in Assets, not
        // the CCK's and not a package's.
        public static bool UserOwned(AnimationClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }
            path = path.Replace('\\', '/');
            return path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                   && !path.StartsWith("Assets/CVR.CCK/", StringComparison.OrdinalIgnoreCase)
                   && !path.StartsWith("Assets/ABI.CCK/", StringComparison.OrdinalIgnoreCase)
                   && !path.StartsWith("Assets/AvatarBridge/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
