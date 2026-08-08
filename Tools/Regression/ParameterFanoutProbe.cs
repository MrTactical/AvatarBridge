#if VRC_SDK_VRCSDK3
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Everything in a converted controller that reacts to one parameter, read through Unity's API
    /// rather than by grepping the YAML.
    ///
    /// Written because hand-parsing the controller file produced two confidently wrong answers in
    /// ten minutes — a transition attributed to the wrong state machine, and a layer credited with
    /// a condition it does not have. The object model knows which layer owns what; the text does
    /// not without care that is easy to get quietly wrong.
    ///
    /// Set AVATARBRIDGE_PROBE_CONTROLLER to the .controller asset path and AVATARBRIDGE_PROBE_PARAM
    /// to the parameter name. Dev tooling; never ships.
    /// </summary>
    public static class ParameterFanoutProbe
    {
        public static void RunBatch()
        {
            string path = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_PROBE_CONTROLLER");
            string wanted = System.Environment.GetEnvironmentVariable("AVATARBRIDGE_PROBE_PARAM");
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(wanted))
            {
                Debug.LogError("[Fanout] set AVATARBRIDGE_PROBE_CONTROLLER and AVATARBRIDGE_PROBE_PARAM");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                Debug.LogError($"[Fanout] no controller at {path}");
                if (Application.isBatchMode) EditorApplication.Exit(3);
                return;
            }

            // Every spelling worth reporting: the parameter itself, the local name the contact
            // bridge gives it, and anything that merely contains the name — a neighbouring
            // parameter whose name starts with this one is exactly the kind of overlap that makes
            // two systems look like one.
            var related = controller.parameters
                .Select(p => p.name)
                .Where(n => n == wanted || n == "#" + wanted || n.Contains(wanted))
                .ToList();
            Debug.Log($"[Fanout] parameters related to \"{wanted}\": {string.Join(", ", related)}");

            foreach (string name in related)
            {
                Debug.Log($"[Fanout] ===== {name} =====");
                int layerIndex = -1;
                foreach (var layer in controller.layers)
                {
                    layerIndex++;
                    if (layer?.stateMachine == null) continue;
                    Walk(layer.stateMachine, layer.name, layerIndex, name, "");
                }
            }

            // The contact components, since the parameter they address is a plain string and is
            // the other half of any "it fires twice" question.
            var animatorType = System.Type.GetType("NAK.Contacts.ContactAnimator, Assembly-CSharp")
                ?? System.AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("NAK.Contacts.ContactAnimator")).FirstOrDefault(t => t != null);
            if (animatorType != null)
            {
                foreach (var go in Object.FindObjectsOfType<GameObject>(true))
                {
                    foreach (var c in go.GetComponents(animatorType))
                    {
                        var f = animatorType.GetField("parameter",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        string p = f?.GetValue(c) as string;
                        if (!string.IsNullOrEmpty(p) && p.Contains(wanted))
                        {
                            Debug.Log($"[Fanout] contact component on \"{go.name}\" drives \"{p}\"");
                        }
                    }
                }
            }

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static void Walk(AnimatorStateMachine machine, string layerName, int layerIndex,
            string param, string prefix)
        {
            foreach (var t in machine.anyStateTransitions)
            {
                Report(t.conditions, t.destinationState, layerName, layerIndex, param,
                    prefix + "AnyState", t.canTransitionToSelf, t.hasExitTime, t.duration);
            }
            foreach (var child in machine.states)
            {
                var state = child.state;
                if (state == null) continue;
                foreach (var t in state.transitions)
                {
                    Report(t.conditions, t.destinationState, layerName, layerIndex, param,
                        prefix + state.name, t.canTransitionToSelf, t.hasExitTime, t.duration);
                }
                // Drivers that WRITE the parameter, which is the other way a value can arrive twice.
                foreach (var b in state.behaviours)
                {
                    if (b == null) continue;
                    var so = new SerializedObject(b);
                    var it = so.GetIterator();
                    while (it.NextVisible(true))
                    {
                        if (it.propertyType == SerializedPropertyType.String
                            && it.stringValue == param
                            && it.propertyPath.ToLowerInvariant().Contains("target"))
                        {
                            Debug.Log($"[Fanout]   WRITES  layer {layerIndex} \"{layerName}\" " +
                                      $"state \"{state.name}\"  behaviour {b.GetType().Name}");
                        }
                    }
                }
            }
            foreach (var sub in machine.stateMachines)
            {
                if (sub.stateMachine != null)
                {
                    Walk(sub.stateMachine, layerName, layerIndex, param, prefix + sub.stateMachine.name + "/");
                }
            }
        }

        static void Report(AnimatorCondition[] conditions, AnimatorState dst, string layerName,
            int layerIndex, string param, string from, bool self, bool exitTime, float duration)
        {
            foreach (var c in conditions)
            {
                if (c.parameter != param) continue;
                Debug.Log($"[Fanout]   READS   layer {layerIndex} \"{layerName}\"  " +
                          $"{from} -> {(dst != null ? dst.name : "(exit)")}  " +
                          $"[{c.mode} {c.threshold}]  self={self} exitTime={exitTime} dur={duration}");
            }
        }
    }
}
#endif
