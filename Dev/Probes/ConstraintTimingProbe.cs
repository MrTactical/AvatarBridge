// Would the constraint guard have fired, on the SOURCE avatar?
//
// SkipConstraintDrivenChain fired on 25 corpus avatars but not the one rigged
// from constraints. Asks the guard's question of the unconverted source.
//
//   -executeMethod AvatarBridge.Regression.ConstraintTimingProbe.Run
//   AVATARBRIDGE_SURVEY_SCENE = the source scene
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace AvatarBridge.Regression
{
    public static class ConstraintTimingProbe
    {
        public static void Run()
        {
            string scene = Environment.GetEnvironmentVariable("AVATARBRIDGE_SURVEY_SCENE");
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);

            foreach (var pb in UnityEngine.Object.FindObjectsOfType<VRCPhysBone>(true))
            {
                var root = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                if (root == null) continue;

                // The guard's own predicate, unchanged.
                string found = null;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    foreach (var component in t.GetComponents<Component>())
                    {
                        if (component == null) continue;
                        bool isConstraint = component is UnityEngine.Animations.IConstraint
                            || (component.GetType().Name.StartsWith("VRC", StringComparison.Ordinal)
                                && component.GetType().Name.EndsWith("Constraint", StringComparison.Ordinal));
                        if (isConstraint) { found = $"{component.GetType().Name} on {t.name}"; break; }
                    }
                    if (found != null) break;
                }

                // Everything constraint-shaped under the root, whatever it is
                // called, so a type the predicate misses shows up here.
                var anythingConstraintish = root.GetComponentsInChildren<Component>(true)
                    .Where(c => c != null && c.GetType().Name.IndexOf("constraint",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(c => c.GetType().FullName)
                    .Distinct()
                    .ToList();

                Debug.Log($"[Timing] chain \"{root.name}\" | guard would fire: {(found ?? "NO")} " +
                          $"| constraint-shaped types under it: " +
                          (anythingConstraintish.Count == 0 ? "none"
                              : string.Join(", ", anythingConstraintish)));
            }

            EditorApplication.Exit(0);
        }
    }
}
#endif
