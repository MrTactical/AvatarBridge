// A vertex shader cannot read a blendshape weight or a bone's scale, and the
// bake is the rest pose, so clips that resize the plug also write the change
// onto its material. The converter runs this on clip copies, the toolkit on
// the avatar's own clips.
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
        static string WeightPack(int shape)
        {
            return shape < 4 ? "_YAPS_ShapeWeights"
                 : shape < 8 ? "_YAPS_ShapeWeights2"
                 : shape < 12 ? "_YAPS_ShapeWeights3"
                 : "_YAPS_ShapeWeights4";
        }

        // One curve on the renderer reaches every slot. "material[1]._X"
        // binds to nothing in Unity, so those stale curves are removed.
        static void SetOnSlots(AnimationClip clip, string rendererPath, Renderer renderer, string property,
            string component, AnimationCurve curve)
        {
            var type = renderer != null ? renderer.GetType() : typeof(SkinnedMeshRenderer);
            string bound = YapsToggles.Bound(property) + component;
            AnimationUtility.SetEditorCurve(clip, new EditorCurveBinding
            {
                path = rendererPath, type = type, propertyName = bound,
            }, curve);
            foreach (var dead in AnimationUtility.GetCurveBindings(clip)
                         .Where(b => b.path == rendererPath && b.propertyName != bound
                                     && YapsToggles.Bare(b.propertyName) == bound).ToList())
            {
                AnimationUtility.SetEditorCurve(clip, dead, null);
            }
        }

        // A curve reaches every slot of the renderer, so the second plug on a
        // shared mesh (lane 1) fills the sixteen slots from the top and has
        // scale and girth of its own. The shader reads the same way.
        public static int ShapeSlot(int index, int lane) => lane > 0 ? (YapsBaker.MaxShapes - 1) ^ index : index;

        static string ScaleName(int lane) => lane > 0 ? "_YAPS_BakeScale2" : "_YAPS_BakeScale";
        static string GirthName(int lane) => lane > 0 ? "_YAPS_BakeGirth2" : "_YAPS_BakeGirth";

        // Older builds wrote the second plug's curve under the first plug's
        // name. Only a curve equal to this plug's own, where the first plug
        // does not write it, is taken as stale.
        static bool PurgeOwnLegacy(AnimationClip clip, string rendererPath, string property, string component,
            AnimationCurve now)
        {
            string bound = YapsToggles.Bound(property) + component;
            bool purged = false;
            foreach (var b in AnimationUtility.GetCurveBindings(clip)
                         .Where(b => b.path == rendererPath && YapsToggles.Bare(b.propertyName) == bound).ToList())
            {
                var old = AnimationUtility.GetEditorCurve(clip, b);
                if (old == null || old.length != now.length) continue;
                bool same = true;
                for (int i = 0; i < old.length && same; i++)
                    same = Mathf.Approximately(old[i].time, now[i].time) && Mathf.Approximately(old[i].value, now[i].value);
                if (!same) continue;
                AnimationUtility.SetEditorCurve(clip, b, null);
                purged = true;
            }
            return purged;
        }

        // Set when a lane-1 mirror took out its own pre-lane curve, so the
        // other plug on the mesh wants mirroring again.
        public static bool Purged;

        // Shape curves on the renderer become weight curves on its material.
        // Returns how many were written; `missed` names animated shapes that
        // move the plug but are not in the bake.
        public static int MirrorShapes(IEnumerable<AnimationClip> clips, string rendererPath, Renderer renderer,
            IList<string> bakedShapes, ICollection<string> movingShapes, ISet<string> missed, int lane = 0,
            IList<string> firstShapes = null)
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
                    int index = bakedShapes.IndexOf(shape);
                    if (index < 0)
                    {
                        if (movingShapes != null && movingShapes.Contains(shape))
                        {
                            missed?.Add(shape);
                        }
                        continue;
                    }
                    // Blendshapes animate 0..100; the bake stores full
                    // shapes, so the material takes 0..1.
                    var scaled = Scaled(AnimationUtility.GetEditorCurve(clip, binding), 0.01f);
                    // firstShapes null: nothing known of the first plug, so nothing is taken.
                    if (lane > 0 && firstShapes != null && (index >= firstShapes.Count || firstShapes[index] != shape))
                        Purged |= PurgeOwnLegacy(clip, rendererPath, WeightPack(index), "." + "xyzw"[index & 3], scaled);
                    int slot = ShapeSlot(index, lane);
                    SetOnSlots(clip, rendererPath, renderer, WeightPack(slot), "." + "xyzw"[slot & 3], scaled);
                    written++;
                }
            }
            return written;
        }

        // The bone's local axis along the shaft: 0 x, 1 y, 2 z.
        public static int AlongAxis(Transform bone, Quaternion bakeRotation)
        {
            if (bone == null) return 2;
            var local = Quaternion.Inverse(bone.rotation) * (bakeRotation * Vector3.forward);
            float ax = Mathf.Abs(local.x), ay = Mathf.Abs(local.y), az = Mathf.Abs(local.z);
            return ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
        }

        // The root's scale curves become bake scale (length axis) and girth
        // (radial axis), one bone per clip. Written as a RATIO to the baked
        // pose: copied raw, a bone baked at 0.4 was scaled twice and drew the
        // plug at 40 percent in game while the editor looked right.
        public static int MirrorBoneScale(IEnumerable<AnimationClip> clips,
            IDictionary<string, Transform> bones, string rendererPath, Renderer renderer,
            Quaternion bakeRotation, int lane = 0, bool purgeLegacy = false)
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
                // The bone this clip scales, if it is one of the plug's.
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
                    var ratio = AsRatio(length, bone.localScale[along]);
                    if (lane > 0 && purgeLegacy) Purged |= PurgeOwnLegacy(clip, rendererPath, "_YAPS_BakeScale", "", ratio);
                    SetOnSlots(clip, rendererPath, renderer, ScaleName(lane), "", ratio);
                    any = true;
                }
                if (girth != null)
                {
                    var ratio = AsRatio(girth, bone.localScale[girthAxis]);
                    if (lane > 0 && purgeLegacy) Purged |= PurgeOwnLegacy(clip, rendererPath, "_YAPS_BakeGirth", "", ratio);
                    SetOnSlots(clip, rendererPath, renderer, GirthName(lane), "", ratio);
                    any = true;
                }
                if (any)
                {
                    written++;
                }
            }
            return written;
        }

        // 1 means "as baked". Tangents are divided too or the curve kinks.
        static AnimationCurve AsRatio(AnimationCurve curve, float atBake)
        {
            // Signed: a mirrored bone animates from -1, and dividing by the
            // magnitude handed the shader -1 for "as baked".
            float divisor = Mathf.Abs(atBake) < 1e-4f ? Mathf.Sign(atBake) * 1e-4f : atBake;
            return Scaled(curve, Mathf.Abs(divisor - 1f) > 1e-4f ? 1f / divisor : 1f);
        }

        // On a copy of the keys: rebuilding each Keyframe from four numbers
        // dropped the weighted tangents and changed the easing.
        static AnimationCurve Scaled(AnimationCurve curve, float factor)
        {
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].value *= factor;
                keys[i].inTangent *= factor;
                keys[i].outTangent *= factor;
            }
            return new AnimationCurve(keys);
        }

        // Any "enabled" on the plug's own object means the plug. Matching only
        // the toolkit's component missed vendor clips animating the ORIGINAL
        // plug component, which conversion removed, so the author's toggle
        // never moved the material gate. A null type (missing script) is
        // exactly that case.
        static bool MeansThePlug(Type type, Type ours)
        {
            if (type == ours || type == null) return true;
            string n = type.Name;
            return n.IndexOf("Plug", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Penetrator", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Animating the component can never work: ChilloutVR strips it at
        // upload and the deform lives in the material. The original curve
        // stays, since it costs nothing.
        public static int MirrorEnabled(IEnumerable<AnimationClip> clips, string componentPath, Type componentType,
            string rendererPath, Renderer renderer, string property)
        {
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
                    else if (b.path == rendererPath && YapsToggles.Writes(b, property))
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
                SetOnSlots(clip, rendererPath, renderer, property, "", new AnimationCurve(source.keys));
                written++;
            }
            return written;
        }

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
