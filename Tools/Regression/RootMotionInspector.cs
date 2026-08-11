#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Dumps the root-motion curves of a converted controller's states, so "did the descent
    // survive?" is answered by Unity's own curve API rather than by parsing controller YAML .
    // which I got wrong twice, once producing a 104-metre spread on a curve measured in metres.
    public static class RootMotionInspector
    {
        [MenuItem("Tools/AvatarBridge Dev/Inspect — root motion on transplanted poses")]
        public static void Run()
        {
            string path = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_CONTROLLER");
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[RootMotion] set AVATARBRIDGE_CONTROLLER to the .controller path");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                Debug.LogError($"[RootMotion] could not load {path}");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (var child in layer.stateMachine.states)
                {
                    var state = child.state;
                    if (state == null || !(state.motion is AnimationClip clip)) continue;
                    if (!state.name.Contains("[AB]")) continue;

                    foreach (var axis in new[]
                    {
                        "RootT.x", "RootT.y", "RootT.z",
                        "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w",
                    })
                    {
                        var binding = AnimationUtility.GetCurveBindings(clip)
                            .FirstOrDefault(b => b.propertyName == axis && string.IsNullOrEmpty(b.path));
                        if (string.IsNullOrEmpty(binding.propertyName)) continue;

                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        if (curve == null || curve.keys.Length == 0) continue;

                        float min = curve.keys.Min(k => k.value);
                        float max = curve.keys.Max(k => k.value);
                        float spread = max - min;
                        Debug.Log($"[RootMotion] {state.name} / {clip.name} · {axis}: " +
                                  $"keys={curve.keys.Length} min={min:0.###} max={max:0.###} " +
                                  $"spread={spread:0.###} -> {(spread > 0.001f ? "MOVES" : "flattened")}");
                    }
                }
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
#endif
