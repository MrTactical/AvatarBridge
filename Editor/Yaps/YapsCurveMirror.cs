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

        // The root bone's scale curve becomes the bake scale on the material.
        // One axis, x first: a size slider scales uniformly, and the shader
        // takes one number. Bones are given as paths from the animator root.
        public static int MirrorBoneScale(IEnumerable<AnimationClip> clips, IEnumerable<string> bonePaths,
            string rendererPath, Type rendererType)
        {
            var bones = new HashSet<string>(bonePaths.Where(p => p != null), StringComparer.Ordinal);
            if (bones.Count == 0)
            {
                return 0;
            }
            int written = 0;
            foreach (var clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }
                AnimationCurve scale = null;
                foreach (var axis in new[] { "m_LocalScale.x", "m_LocalScale.z", "m_LocalScale.y" })
                {
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (binding.type == typeof(Transform) && binding.propertyName == axis
                            && bones.Contains(binding.path))
                        {
                            scale = AnimationUtility.GetEditorCurve(clip, binding);
                            break;
                        }
                    }
                    if (scale != null)
                    {
                        break;
                    }
                }
                if (scale == null)
                {
                    continue;
                }
                AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                {
                    path = rendererPath,
                    type = rendererType,
                    propertyName = "material._YAPS_BakeScale",
                }, new AnimationCurve(scale.keys));
                written++;
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
