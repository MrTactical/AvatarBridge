// Checks a YAPS prop's channel is actually wired, and repairs the one thing
// that is known to come undone on its own.
//
// THE BUG THIS EXISTS FOR
//
// A CVRSpawnableValue reaches its animator through animatorParameterName,
// and the client will not move a value whose name is blank:
//
//     if (... || string.IsNullOrEmpty(spawnableValue.animatorParameterName)
//         || ...) return;
//
// The CCK's own inspector writes that field back on EVERY repaint:
//
//     animatorParameterNameProp.stringValue = AdvancedDropdownInput(
//         rect, animatorParameterNameProp.stringValue,
//         CVRCommon.GetParametersFromAnimatorAsString(animator), ...);
//
// and the list it offers comes from animator.parameters, which is empty for
// a prefab asset whose Animator has never been initialised. So selecting the
// prop to CHECK the channel is what breaks it, and it blanks the row the
// list happens to be drawing — the first one, which is engagement.
//
// The damage is invisible from every angle that matters. No error, no
// warning, and the inspector renders the loss as an empty dropdown, which
// reads as a control nobody has touched rather than as data that was there a
// moment ago. In game the plug simply stops reacting to contact-only sockets
// while continuing to work perfectly on lit ones, because the light path
// never touches this value.
//
// Two prefabs built from the same script settled it: the project whose
// prefab had been open in the inspector had a blank name, the one that had
// not kept "E".
//
// The parameter list here is read from the CONTROLLER ASSET rather than from
// animator.parameters, which is the whole reason this can repair something
// the inspector cannot.
#if UNITY_EDITOR && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Spike
{
    public static class YapsPropVerify
    {
        const string Dir = "Assets/SpsSpike/Props";

        [MenuItem("Tools/Avatar Bridge/Spike/Verify and repair YAPS props")]
        public static void Run()
        {
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { Dir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .ToList();

            if (paths.Count == 0)
            {
                Debug.LogWarning("[YAPS] No prop prefabs found in " + Dir +
                                 ". Build the test props first.");
                return;
            }

            int repaired = 0, broken = 0;
            var report = new System.Text.StringBuilder();
            report.AppendLine("[YAPS] Prop channel check over " + paths.Count + " prefab(s).");

            foreach (string path in paths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                try
                {
                    foreach (var spawnable in root.GetComponentsInChildren<CVRSpawnable>(true))
                    {
                        Check(spawnable, path, report, ref changed, ref repaired, ref broken);
                    }
                    CheckTriggerHosts(root, path, report, ref broken);

                    if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            if (repaired > 0) AssetDatabase.SaveAssets();

            report.AppendLine(repaired == 0 && broken == 0
                ? "  Every channel is wired. Nothing to do."
                : $"  repaired {repaired}, still broken {broken}.");
            if (repaired > 0)
            {
                report.AppendLine("  The repaired props must be re-uploaded — a prefab fixed on " +
                                  "disk changes nothing about the copy already on the server.");
                report.AppendLine("  And do not leave the prop selected with the CCK inspector " +
                                  "open: drawing that list is what blanks the name.");
            }

            if (broken > 0) Debug.LogError(report.ToString());
            else if (repaired > 0) Debug.LogWarning(report.ToString());
            else Debug.Log(report.ToString());
        }

        static void Check(CVRSpawnable spawnable, string path, System.Text.StringBuilder report,
            ref bool changed, ref int repaired, ref int broken)
        {
            var values = spawnable.syncValues;
            if (values == null || values.Count == 0) return;

            string name = System.IO.Path.GetFileNameWithoutExtension(path);

            // Filling the list is not enough. Without this the client never
            // looks at it, and every value below is inert however well named.
            if (!spawnable.useAdditionalValues)
            {
                spawnable.useAdditionalValues = true;
                changed = true;
                repaired++;
                report.AppendLine($"  {name}: useAdditionalValues was OFF — the whole value list " +
                                  "was being ignored. Turned on.");
            }

            for (int i = 0; i < values.Count; i++)
            {
                var value = values[i];
                string label = $"  {name}: value {i} \"{value.name}\"";

                if (value.animator == null)
                {
                    // Not repairable by guessing: which animator a value
                    // belongs to is a decision, not a lookup.
                    report.AppendLine(label + " has no animator, so it drives nothing.");
                    broken++;
                    continue;
                }

                var controller = value.animator.runtimeAnimatorController as AnimatorController;
                if (controller == null)
                {
                    report.AppendLine(label + " points at an animator with no controller asset.");
                    broken++;
                    continue;
                }

                var declared = new HashSet<string>(controller.parameters.Select(p => p.name));

                if (string.IsNullOrEmpty(value.animatorParameterName)
                    || value.animatorParameterName == "-none-")
                {
                    // The repair. The value's own name is what the builder
                    // used, so it is the right answer whenever the controller
                    // agrees — and refusing to invent one when it does not is
                    // the point, because a wrong name fails exactly as
                    // silently as a blank one.
                    if (declared.Contains(value.name))
                    {
                        value.animatorParameterName = value.name;
                        changed = true;
                        repaired++;
                        report.AppendLine(label + " had a BLANK parameter name — the client " +
                                          "skips those, so this value never moved. Restored to \"" +
                                          value.name + "\".");
                    }
                    else
                    {
                        report.AppendLine(label + " has a blank parameter name and the controller " +
                                          "declares nothing called \"" + value.name +
                                          "\", so there is no safe answer to restore.");
                        broken++;
                    }
                    continue;
                }

                if (!declared.Contains(value.animatorParameterName))
                {
                    report.AppendLine(label + " names \"" + value.animatorParameterName +
                                      "\", which the controller does not declare.");
                    broken++;
                }
            }
        }

        // One GameObject per trigger. The client adds a ContactReceiver to
        // the trigger's own object and gives it that trigger's shape, so two
        // triggers sharing an object means two receivers fighting over one
        // shape and a channel that reports nothing. It was silent when it
        // happened, which is why it is checked rather than remembered.
        static void CheckTriggerHosts(GameObject root, string path,
            System.Text.StringBuilder report, ref int broken)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int count = t.GetComponents<CVRSpawnableTrigger>().Length;
                if (count > 1)
                {
                    report.AppendLine($"  {name}: \"{t.name}\" carries {count} triggers. Only one " +
                                      "of them will work — give each its own object.");
                    broken++;
                }
            }
        }
    }
}
#endif
