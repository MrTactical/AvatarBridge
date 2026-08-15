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
// The CCK's synced-value inspector assigns that field back on every draw:
//
//     animatorParameterNameProp.stringValue = AdvancedDropdownInput(...);
//
// and underneath the dropdown it is an editable EditorGUI.TextField, not a
// fixed list. It returns the value unchanged when nobody touches it, so this
// is not an automatic rewrite — but it IS a live text box sitting in a
// reorderable list, where a stray click or keystroke clears a name with no
// undo anyone would notice.
//
// What is certain rather than inferred: the builder sets all eight names,
// and two prefabs built from that same script disagreed — the project whose
// prop had been open in the inspector had a blank first name, the one that
// had not kept "E". So the value does not survive inspection, whatever the
// exact mechanism, and the blank is what the client acts on.
//
// The damage is invisible from every angle that matters. No error, no
// warning, and the row renders as an empty field, which reads as a control
// nobody has filled in rather than data that was there a moment ago. In game
// the plug simply stops reacting to contact-only sockets while continuing to
// work perfectly on lit ones, because the light path never touches this
// value.
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

        [MenuItem("AvatarBridge/Spike/Verify and repair YAPS props")]
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
