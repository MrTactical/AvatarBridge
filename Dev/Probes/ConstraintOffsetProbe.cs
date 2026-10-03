// What a VRCParentConstraint's offsets are called in an animation clip, and
// whether the value a curve writes means the same thing as on Unity's
// ParentConstraint (degrees and metres, no conversion).
//
// ConstraintConverter maps the per-source offset curves onto Unity's arrays.
// The spelling and the units are measured here rather than assumed: a wrong
// name binds nothing and says nothing.
//
//   -executeMethod AvatarBridge.Regression.ConstraintOffsetProbe.Run
#if VRC_SDK_VRCSDK3
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;

namespace AvatarBridge.Regression
{
    public static class ConstraintOffsetProbe
    {
        const float RotY = 30f;
        const float PosX = 0.25f;

        public static void Run()
        {
            try
            {
                Probe();
            }
            catch (Exception e)
            {
                Debug.LogError("PROBE failed: " + e);
            }
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        static void Probe()
        {
            var root = new GameObject("Root");
            var a = Child(root, "SourceA");
            var b = Child(root, "SourceB");

            var vrcHost = Child(root, "VrcDriven");
            var vrc = vrcHost.gameObject.AddComponent<VRCParentConstraint>();
            vrc.Sources.Add(new VRCConstraintSource { SourceTransform = a, Weight = 1f });
            vrc.Sources.Add(new VRCConstraintSource { SourceTransform = b, Weight = 1f });

            var unityHost = Child(root, "UnityDriven");
            var unity = unityHost.gameObject.AddComponent<ParentConstraint>();
            unity.AddSource(new ConstraintSource { sourceTransform = a, weight = 1f });
            unity.AddSource(new ConstraintSource { sourceTransform = b, weight = 1f });

            LogBindings(root, vrcHost, typeof(VRCParentConstraint));
            LogBindings(root, unityHost, typeof(ParentConstraint));

            // The same numbers through each spelling, sampled, then read back off the component.
            var clip = new AnimationClip();
            Set(clip, "VrcDriven", typeof(VRCParentConstraint), "Sources.source1.ParentRotationOffset.y", RotY);
            Set(clip, "VrcDriven", typeof(VRCParentConstraint), "Sources.source1.ParentPositionOffset.x", PosX);
            Set(clip, "UnityDriven", typeof(ParentConstraint), "m_RotationOffsets.Array.data[1].y", RotY);
            Set(clip, "UnityDriven", typeof(ParentConstraint), "m_TranslationOffsets.Array.data[1].x", PosX);

            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(root, clip, 0f);
                AnimationMode.EndSampling();

                var vrcRot = vrc.Sources[1].ParentRotationOffset;
                var vrcPos = vrc.Sources[1].ParentPositionOffset;
                var unityRot = unity.GetRotationOffset(1);
                var unityPos = unity.GetTranslationOffset(1);
                Debug.Log($"PROBE sampled VRC   source1 rot {vrcRot} pos {vrcPos}");
                Debug.Log($"PROBE sampled Unity source1 rot {unityRot} pos {unityPos}");
                bool vrcTook = Mathf.Approximately(vrcRot.y, RotY) && Mathf.Approximately(vrcPos.x, PosX);
                bool unityTook = Mathf.Approximately(unityRot.y, RotY) && Mathf.Approximately(unityPos.x, PosX);
                Debug.Log("PROBE verdict: " + (
                    vrcTook && unityTook
                        ? "both read the curve value back unchanged: same units, map 1:1"
                        : !vrcTook && !unityTook
                            ? "INCONCLUSIVE, sampling reached neither component"
                            : $"they differ (VRC took it: {vrcTook}, Unity took it: {unityTook})"));
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
            UnityEngine.Object.DestroyImmediate(root);
        }

        static Transform Child(GameObject root, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root.transform, false);
            return t;
        }

        static void Set(AnimationClip clip, string path, Type type, string property, float value)
        {
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, type, property), AnimationCurve.Constant(0f, 1f, value));
        }

        static void LogBindings(GameObject root, Transform host, Type type)
        {
            foreach (var binding in AnimationUtility.GetAnimatableBindings(host.gameObject, root))
            {
                if (binding.type == type)
                {
                    Debug.Log($"PROBE binding {type.Name} {binding.propertyName}");
                }
            }
        }
    }
}
#endif
