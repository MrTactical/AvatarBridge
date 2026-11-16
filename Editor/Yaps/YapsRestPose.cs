// The bake measures the plug as the scene holds it, and the markers carry that
// length whatever a size slider says. In game the slider starts at its menu
// default. So before measuring, each size bone is set to the scale the avatar's
// own animator gives it with every parameter at its default, which is how the
// avatar loads. A no-op in game, where the resting state writes that value anyway.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge
{
    public static class YapsRestPose
    {
        // Null when it could not look; else one line per size bone it moved.
        public static List<string> Apply(Transform animRoot, RuntimeAnimatorController controller,
            IDictionary<string, Transform> bones)
        {
            var moved = new List<string>();
            if (controller == null || bones == null || bones.Count == 0 || !controller.animationClips.Any(c => Scales(c, bones)))
            {
                return moved;
            }
            if (animRoot == null)
            {
                return null;
            }

            // A bare copy of the hierarchy, transforms only. Clips bind by path, so
            // the size curves land on it, and nothing of the avatar's runs or is touched.
            var twins = new Dictionary<Transform, Transform>();
            var copy = Twin(animRoot, null, twins);
            try
            {
                var animator = copy.gameObject.AddComponent<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.runtimeAnimatorController = controller;
                animator.Rebind();
                if (!animator.isInitialized)
                {
                    return null;
                }
                // A second, for transitions out of an entry state to finish.
                for (int i = 0; i < 60; i++)
                {
                    animator.Update(1f / 60f);
                }
                foreach (var bone in bones.Values.Where(b => b != null).Distinct())
                {
                    if (!twins.TryGetValue(bone, out var twin))
                    {
                        continue;
                    }
                    Vector3 from = bone.localScale, to = twin.localScale;
                    if ((from - to).sqrMagnitude < 1e-8f)
                    {
                        continue;
                    }
                    Undo.RecordObject(bone, "Set plug to its resting size");
                    bone.localScale = to;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
                    moved.Add($"{bone.name} {Show(from)} to {Show(to)}");
                }
                return moved;
            }
            finally
            {
                Object.DestroyImmediate(copy.gameObject);
            }
        }

        static bool Scales(AnimationClip clip, IDictionary<string, Transform> bones) =>
            clip != null && AnimationUtility.GetCurveBindings(clip).Any(b => b.type == typeof(Transform)
                && b.propertyName.StartsWith("m_LocalScale.", System.StringComparison.Ordinal) && bones.ContainsKey(b.path));

        static Transform Twin(Transform source, Transform parent, Dictionary<Transform, Transform> twins)
        {
            var t = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave }.transform;
            t.SetParent(parent, false);
            t.localPosition = source.localPosition;
            t.localRotation = source.localRotation;
            t.localScale = source.localScale;
            twins[source] = t;
            for (int i = 0; i < source.childCount; i++)
            {
                Twin(source.GetChild(i), t, twins);
            }
            return t;
        }

        static string Show(Vector3 v) =>
            Mathf.Approximately(v.x, v.y) && Mathf.Approximately(v.y, v.z) ? v.x.ToString("0.###") : $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
    }
}
#endif
