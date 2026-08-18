// Phase 1 of the optimisation plan: the removals nothing can notice.
//
// Two of them, both proven by reading the controller rather than by
// flipping something and watching:
//
//   - a layer with no states, which can never do anything
//   - a parameter no clip writes, no transition reads, no driver touches,
//     no menu control names and no contact fires
//
// The third item on the plan's list, the placeholder clips written into
// empty motion slots, is NOT removed and must not be. Unity crashes when
// it builds a playable graph containing an empty slot, which is why those
// placeholders exist at all. They are reported so the missing motion gets
// found, and left exactly where they are.
//
// A sweep result is never the criterion here. An individual toggle
// subordinated to a preset, a driver behind a wait state, a local name
// driven only from elsewhere: all three look dead in motion and are not.
// Only the static reading decides.
#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge
{
    public static class FreeWins
    {
        const string Category = "Free wins";

        static readonly System.Text.RegularExpressions.Regex YapsOwned =
            new System.Text.RegularExpressions.Regex(@"^#?YAPS\d");

        public class Plan
        {
            public AnimatorController Controller;
            public readonly List<string> Layers = new List<string>();
            public readonly List<string> Parameters = new List<string>();
            public readonly List<string> Spared = new List<string>();
            public int PlaceholderStates;
            public bool Any => Layers.Count > 0 || Parameters.Count > 0;
        }

        public static Plan Find(CVRAvatar avatar, AvatarSurvey.Model model)
        {
            var plan = new Plan();
            var animator = avatar != null ? avatar.GetComponent<Animator>() : null;
            plan.Controller = Underlying(animator != null ? animator.runtimeAnimatorController : null);
            if (plan.Controller == null) return plan;

            var named = MenuNames(avatar);

            foreach (var finding in model.Findings)
            {
                if (finding.Kind == "empty layer")
                {
                    // The base layer stays whatever it holds: it carries the
                    // default weight every other layer is written against.
                    var layer = model.Layers.FirstOrDefault(l => l.Name == finding.Subject);
                    if (layer != null && layer.Index == 0)
                    {
                        plan.Spared.Add($"\"{finding.Subject}\" is the base layer");
                        continue;
                    }
                    plan.Layers.Add(finding.Subject);
                }
                else if (finding.Kind == "unused parameter")
                {
                    string name = finding.Subject;
                    if (CvrParameterNames.IsGameDriven(name))
                    {
                        plan.Spared.Add($"\"{name}\" is written by the game, not by this avatar");
                        continue;
                    }
                    if (YapsOwned.IsMatch(name))
                    {
                        plan.Spared.Add($"\"{name}\" belongs to YAPS, which fills its channel at setup");
                        continue;
                    }
                    // Belt and braces. The survey counts a menu entry as a
                    // writer, so this should never fire; if it ever does, the
                    // parameter stays and the disagreement is worth knowing.
                    if (named.Contains(name))
                    {
                        plan.Spared.Add($"\"{name}\" is named by an Advanced Settings entry");
                        continue;
                    }
                    plan.Parameters.Add(name);
                }
            }

            plan.PlaceholderStates = CountPlaceholders(plan.Controller);
            return plan;
        }

        // Into a copy at `savePath`, or into the controller itself when it
        // is null. The Toolkit always passes a path: someone pointing this
        // at their own avatar should be able to throw the result away.
        public static AnimatorController Apply(CVRAvatar avatar, Plan plan, string savePath, BridgeReport report)
        {
            if (plan == null || plan.Controller == null)
            {
                report.Error(Category, "No animator controller", "Nothing to read, so nothing to tidy.");
                return null;
            }

            var into = plan.Controller;
            if (!string.IsNullOrEmpty(savePath))
            {
                string from = AssetDatabase.GetAssetPath(plan.Controller);
                // CopyAsset fails silently into a folder that is not there.
                System.IO.Directory.CreateDirectory(System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "..", System.IO.Path.GetDirectoryName(savePath))));
                AssetDatabase.Refresh();
                if (string.IsNullOrEmpty(from) || !AssetDatabase.CopyAsset(from, savePath))
                {
                    report.Error(Category, "Could not copy the controller", $"\"{from}\" to \"{savePath}\"");
                    return null;
                }
                into = AssetDatabase.LoadAssetAtPath<AnimatorController>(savePath);
            }

            int layers = 0;
            foreach (string name in plan.Layers)
            {
                int index = System.Array.FindIndex(into.layers, l => l.name == name);
                if (index < 0) continue;
                into.RemoveLayer(index);
                layers++;
            }

            int parameters = 0;
            foreach (string name in plan.Parameters)
            {
                int index = System.Array.FindIndex(into.parameters, p => p.name == name);
                if (index < 0) continue;
                into.RemoveParameter(index);
                parameters++;
            }

            EditorUtility.SetDirty(into);
            AssetDatabase.SaveAssets();

            if (into != plan.Controller)
            {
                var animator = avatar.GetComponent<Animator>();
                // An avatar usually runs an override controller wrapping the
                // base. Assigning over the top of that would throw away every
                // clip mapping in it, so the swap happens one level down.
                if (animator.runtimeAnimatorController is AnimatorOverrideController over)
                {
                    Undo.RecordObject(over, "Tidy animator");
                    over.runtimeAnimatorController = into;
                    EditorUtility.SetDirty(over);
                }
                else
                {
                    Undo.RecordObject(animator, "Tidy animator");
                    animator.runtimeAnimatorController = into;
                    EditorUtility.SetDirty(animator);
                }
            }

            Fill(report, plan, layers, parameters, into);
            return into;
        }

        public static void Fill(BridgeReport report, Plan plan, int layers, int parameters, AnimatorController into)
        {
            if (layers > 0)
            {
                report.Converted(Category, $"{layers} layer(s) with no states removed",
                    string.Join(", ", plan.Layers) + ". A layer with no states has nothing to play, " +
                    "so nothing it did can be missed.");
            }
            if (parameters > 0)
            {
                report.Converted(Category, $"{parameters} parameter(s) nothing touches removed",
                    string.Join(", ", plan.Parameters) + ". No clip writes them, no transition reads " +
                    "them, no driver, menu control or contact names them.");
            }
            foreach (string spared in plan.Spared)
            {
                report.Approximated(Category, "Left alone", spared);
            }
            if (plan.PlaceholderStates > 0)
            {
                report.Warning(Category, $"{plan.PlaceholderStates} slot(s) still hold a placeholder clip",
                    "These are NOT waste and are not removed. Unity crashes when it builds a playable " +
                    "graph containing an empty motion slot, so each of these holds a clip that animates " +
                    "one inert value instead. What they mean is that a motion the author intended never " +
                    "arrived — usually an asset that went missing or a build step that did not run. " +
                    "Find out why before you rely on whatever used them.");
            }
            if (into != null)
            {
                report.Converted(Category, "Written to a copy", AssetDatabase.GetAssetPath(into));
            }
        }

        static int CountPlaceholders(AnimatorController controller)
        {
            int found = 0;
            foreach (var layer in controller.layers)
            {
                foreach (var clip in Clips(layer.stateMachine))
                {
                    if (IsPlaceholder(clip)) found++;
                }
            }
            return found;
        }

        static bool IsPlaceholder(Motion motion)
        {
            var clip = motion as AnimationClip;
            if (clip == null) return false;
            var bindings = AnimationUtility.GetCurveBindings(clip);
            return bindings.Length > 0
                   && bindings.All(b => b.path != null
                                        && b.path.EndsWith("AvatarBridge_EmptySlot", System.StringComparison.Ordinal));
        }

        // Every motion in the machine, including the ones inside blend trees
        // and sub-machines. Walked rather than listed: an avatar's states are
        // nested however its author nested them.
        static IEnumerable<Motion> Clips(AnimatorStateMachine machine)
        {
            if (machine == null) yield break;
            foreach (var child in machine.states)
            {
                foreach (var motion in Motions(child.state.motion)) yield return motion;
            }
            foreach (var child in machine.stateMachines)
            {
                foreach (var motion in Clips(child.stateMachine)) yield return motion;
            }
        }

        static IEnumerable<Motion> Motions(Motion motion)
        {
            if (motion == null) yield break;
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                {
                    foreach (var inner in Motions(child.motion)) yield return inner;
                }
                yield break;
            }
            yield return motion;
        }

        static HashSet<string> MenuNames(CVRAvatar avatar)
        {
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            if (avatar == null || avatar.avatarSettings == null || avatar.avatarSettings.settings == null) return names;
            foreach (var entry in avatar.avatarSettings.settings)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.machineName)) names.Add(entry.machineName);
            }
            return names;
        }

        static AnimatorController Underlying(RuntimeAnimatorController runtime)
        {
            for (int guard = 0; runtime != null && guard < 8; guard++)
            {
                if (runtime is AnimatorController controller) return controller;
                if (runtime is AnimatorOverrideController over) { runtime = over.runtimeAnimatorController; continue; }
                break;
            }
            return null;
        }
    }
}
#endif
