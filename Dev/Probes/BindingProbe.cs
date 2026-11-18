#if VRC_SDK_VRCSDK3
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Everything that writes one object's bindings, plus every state in the owning layer that
    // writes nothing to it: CVR does not restore Write Defaults, so values stick in silent states.
    // Grepping .anim YAML pairs attributes with the wrong curve; the object model does not.
    //
    // AVATARBRIDGE_PROBE_CONTROLLER  the .controller asset path
    // AVATARBRIDGE_PROBE_PATH        substring of the object path to follow
    // Dev tooling; never ships.
    public static class BindingProbe
    {
        public static void RunBatch()
        {
            string path = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_PROBE_CONTROLLER");
            string wanted = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_PROBE_PATH");
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(wanted))
            {
                Debug.LogError("[Binding] set AVATARBRIDGE_PROBE_CONTROLLER and AVATARBRIDGE_PROBE_PATH");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                Debug.LogError($"[Binding] no controller at {path}");
                if (Application.isBatchMode) EditorApplication.Exit(3);
                return;
            }

            // Clip -> the matching curves it holds, with the value each one LEAVES behind.
            var writers = new Dictionary<AnimationClip, List<string>>();
            foreach (var clip in controller.animationClips.Where(c => c != null).Distinct())
            {
                var hits = new List<string>();
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.path == null || b.path.IndexOf(wanted, System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    string ends = curve != null && curve.length > 0
                        ? curve.keys[curve.length - 1].value.ToString("0.###")
                        : "?";
                    hits.Add($"{b.path} . {b.propertyName} ({b.type.Name}) -> {ends}");
                }
                if (hits.Count > 0) writers[clip] = hits;
            }

            Debug.Log($"[Binding] {writers.Count} clip(s) write \"{wanted}\"");
            foreach (var kv in writers.OrderBy(k => k.Key.name, System.StringComparer.Ordinal))
            {
                Debug.Log($"[Binding] CLIP {kv.Key.name}");
                foreach (string line in kv.Value.OrderBy(s => s, System.StringComparer.Ordinal))
                {
                    Debug.Log($"[Binding]     {line}");
                }
            }

            // The owning layers, and within each the states that write it versus the states that
            // do not. A state in the second list is somewhere the value can latch.
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i];
                if (layer?.stateMachine == null) continue;
                var writes = new List<string>();
                var silent = new List<string>();
                Walk(layer.stateMachine, "", writers, writes, silent);
                if (writes.Count == 0) continue;

                Debug.Log($"[Binding] ===== layer {i} \"{layer.name}\": {writes.Count} state(s) write it, " +
                          $"{silent.Count} say nothing =====");
                foreach (string s in writes.OrderBy(s => s, System.StringComparer.Ordinal))
                {
                    Debug.Log($"[Binding]   WRITES  {s}");
                }
                foreach (string s in silent.OrderBy(s => s, System.StringComparer.Ordinal))
                {
                    Debug.Log($"[Binding]   SILENT  {s}   <- value latches here");
                }
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Walk(AnimatorStateMachine machine, string prefix,
            Dictionary<AnimationClip, List<string>> writers, List<string> writes, List<string> silent)
        {
            foreach (var child in machine.states)
            {
                var state = child.state;
                if (state == null) continue;
                string name = prefix + state.name;
                if (MotionWrites(state.motion, writers))
                {
                    writes.Add(name);
                }
                else
                {
                    // The assert pass skips states with an unconditional exit; if one lingers, it latches.
                    var t = state.transitions ?? new AnimatorStateTransition[0];
                    bool unconditional = t.Any(x => x != null && (x.conditions == null || x.conditions.Length == 0));
                    string why = state.motion == null ? "no motion at all"
                        : state.motion is BlendTree ? "blend tree"
                        : "clip writes nothing here";
                    silent.Add($"{name}  [{why}; {t.Length} exit(s)" +
                               $"{(unconditional ? ", UNCONDITIONAL: assert pass skips this" : "")}" +
                               $"{(t.Length == 0 ? ", NO EXIT: the layer lives here" : "")}]");
                }
            }
            foreach (var sub in machine.stateMachines)
            {
                if (sub.stateMachine != null)
                {
                    Walk(sub.stateMachine, prefix + sub.stateMachine.name + "/", writers, writes, silent);
                }
            }
        }

        static bool MotionWrites(Motion motion, Dictionary<AnimationClip, List<string>> writers)
        {
            if (motion is AnimationClip clip) return writers.ContainsKey(clip);
            if (motion is BlendTree tree)
            {
                return tree.children.Any(c => MotionWrites(c.motion, writers));
            }
            return false;
        }
    }
}
#endif
