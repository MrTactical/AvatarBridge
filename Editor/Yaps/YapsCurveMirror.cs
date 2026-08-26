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
        // as their path from the animator root together with the transform
        // itself, because what the shader wants is a RATIO to the pose the
        // bake measured, not the bone's raw scale.
        //
        // The material carries 1 for "the size it was baked at". A bone's
        // m_LocalScale curve carries an absolute number, so copying it
        // straight across told the truth only for a bone sitting at exactly
        // 1 when it was baked. Every other rig had its bone scale applied a
        // second time, on top of the skinning that had already applied it:
        // a bone baked at 0.4 drew a plug squashed to 40 percent in game,
        // while the editor, where no animator runs, kept the baked size and
        // looked correct. Dividing by the bake pose makes the two agree by
        // construction, and keeps them agreeing wherever a size slider goes.
        public static int MirrorBoneScale(IEnumerable<AnimationClip> clips,
            IDictionary<string, Transform> bones, string rendererPath, Type rendererType,
            Quaternion bakeRotation)
        {
            if (bones == null || bones.Count == 0)
            {
                return 0;
            }
            string[] axes = { "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z" };

            int written = 0;
            foreach (var clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }
                // The bone this clip scales, if any of ours.
                Transform bone = null;
                string bonePath = null;
                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var b in bindings)
                {
                    if (b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalScale.", StringComparison.Ordinal)
                        && bones.TryGetValue(b.path, out bone) && bone != null)
                    {
                        bonePath = b.path;
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
                        if (b.type == typeof(Transform) && b.path == bonePath && b.propertyName == property)
                        {
                            return AnimationUtility.GetEditorCurve(clip, b);
                        }
                    }
                    return null;
                }
                // Per bone, not per chain: a child further down sits at its
                // own angle, and its scale curve is in its own space.
                int along = AlongAxis(bone, bakeRotation);
                var length = Curve(axes[along]);
                AnimationCurve girth = null;
                int girthAxis = 0;
                for (int a = 0; a < 3 && girth == null; a++)
                {
                    if (a == along)
                    {
                        continue;
                    }
                    girth = Curve(axes[a]);
                    girthAxis = a;
                }
                bool any = false;
                if (length != null)
                {
                    AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                    {
                        path = rendererPath, type = rendererType, propertyName = "material._YAPS_BakeScale",
                    }, AsRatio(length, bone.localScale[along]));
                    any = true;
                }
                if (girth != null)
                {
                    AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                    {
                        path = rendererPath, type = rendererType, propertyName = "material._YAPS_BakeGirth",
                    }, AsRatio(girth, bone.localScale[girthAxis]));
                    any = true;
                }
                if (any)
                {
                    written++;
                }
            }
            return written;
        }

        // The same curve read against the size the bake measured, so that 1
        // means "as baked". Tangents are a slope in the same units, so they
        // are divided with the values or the curve kinks between keys.
        static AnimationCurve AsRatio(AnimationCurve curve, float atBake)
        {
            float divisor = Mathf.Max(Mathf.Abs(atBake), 1e-4f);
            var keys = curve.keys;
            if (Mathf.Abs(divisor - 1f) > 1e-4f)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value /= divisor;
                    keys[i].inTangent /= divisor;
                    keys[i].outTangent /= divisor;
                }
            }
            return new AnimationCurve(keys);
        }

        // A curve on the COMPONENT's own Enabled field, mirrored onto the
        // material property that actually does the work.
        //
        // Animating the component is the obvious thing to reach for and it
        // can never work: ChilloutVR strips the component at upload, and the
        // deform lives in the material. Rather than let that fail silently,
        // the bake writes the curve people meant to write. The original is
        // left where it is: it is the user's clip, and a field bound to a
        // component that is not there costs nothing.
        // WHOSE "enabled" this is, on the plug's own object.
        //
        // Matching the toolkit's own component alone missed the case that
        // matters most: a vendor's clip animating the ORIGINAL system's plug
        // component, which conversion replaced. Joe's horse rig ships
        // HRS_COCK_ERECT driving an SPS plug's m_Enabled from an erection
        // slider; YAPS swapped the component underneath it, the binding kept
        // pointing at a script guid no longer in the project, and the mirror
        // ignored it. The slider then switched a plug on that had no way to
        // hear it — nothing in game, on either transport, since the material
        // gate never moved.
        //
        // A curve on the PLUG'S OWN transform saying "enabled" means the
        // plug, whoever wrote it. A missing type (null) is the strongest
        // signal of all: the component it named is gone, which is exactly
        // what conversion does to the system this replaces.
        static bool MeansThePlug(Type type, Type ours)
        {
            if (type == ours || type == null) return true;
            string n = type.Name;
            return n.IndexOf("Plug", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Penetrator", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static int MirrorEnabled(IEnumerable<AnimationClip> clips, string componentPath, Type componentType,
            string rendererPath, Type rendererType, string property)
        {
            string target = "material." + property;
            int written = 0;
            foreach (var clip in clips)
            {
                if (clip == null)
                {
                    continue;
                }
                var bindings = AnimationUtility.GetCurveBindings(clip);
                AnimationCurve source = null;
                bool alreadyDriven = false;
                foreach (var b in bindings)
                {
                    if (b.path == componentPath && b.propertyName == "m_Enabled"
                        && MeansThePlug(b.type, componentType))
                    {
                        source = AnimationUtility.GetEditorCurve(clip, b);
                    }
                    else if (b.path == rendererPath && b.propertyName == target)
                    {
                        alreadyDriven = true;
                    }
                }
                // Somebody who already drives the material means it; never
                // overwrite that with a guess taken from the component.
                if (source == null || alreadyDriven)
                {
                    continue;
                }
                AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
                {
                    path = rendererPath, type = rendererType, propertyName = target,
                }, new AnimationCurve(source.keys));
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
