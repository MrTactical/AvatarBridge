#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Asserts that every ChilloutVR CCK enum member AvatarBridge names in source still exists in
    // the installed CCK.
    //
    // Members bind at compile time, so a CCK rename makes the tool uninstallable (3.7.1 failed
    // where the CCK spells "MoreThen"/"LessThen"); the compile check only sees the local CCK.
    // Run after a CCK update and before a release. When one moves, resolve THAT member by name
    // (see TryOperator in AnimatorMerger). Hand-maintained: a generated list drifts with the source.
    public static class CckContractProbe
    {
        static readonly (string Type, string[] Members)[] Required =
        {
            ("ABI.CCK.Components.AnimatorDriverTask+Operator", new[]
            {
                "Set", "Addition", "Subtraction", "Multiplication", "Division", "Power", "NotEqual",
                // No "MoreThan": the CCK shipped it both ways, so it is resolved by name.
            }),
            ("ABI.CCK.Components.AnimatorDriverTask+ParameterType", new[]
            {
                "Float", "Int", "Bool", "Trigger",
            }),
            ("ABI.CCK.Components.AnimatorDriverTask+SourceType", new[]
            {
                "Static", "Parameter", "Random",
            }),
            ("ABI.CCK.Components.CVRAdvancedSettingsEntry+SettingsType", new[]
            {
                "Toggle", "Dropdown", "Color", "Slider", "Joystick2D", "Joystick3D",
                "InputSingle", "InputVector2", "InputVector3",
            }),
            ("ABI.CCK.Components.CVRAvatar+CVRAvatarVisemeMode", new[]
            {
                "Visemes", "JawBone", "SingleBlendshape",
            }),
            ("ABI.CCK.Components.CVRAvatar+CVRAvatarEyeLookMode", new[]
            {
                "Transform",
            }),
        };

        [MenuItem("Tools/Avatar Bridge/Dev/Checks/CCK still has what we name")]
        public static void Run()
        {
            int missingTypes = 0, missingMembers = 0, checkedMembers = 0;
            foreach (var (typeName, members) in Required)
            {
                var type = System.AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(typeName))
                    .FirstOrDefault(t => t != null);
                if (type == null)
                {
                    Debug.LogError($"[CckContract] TYPE MISSING: {typeName}: the CCK no longer has it, " +
                                   "or moved it. Everything naming it will fail to compile.");
                    missingTypes++;
                    continue;
                }
                var have = new HashSet<string>(System.Enum.GetNames(type));
                foreach (string member in members)
                {
                    checkedMembers++;
                    if (!have.Contains(member))
                    {
                        Debug.LogError($"[CckContract] MEMBER MISSING: {typeName}.{member}: this CCK has " +
                                       $"[{string.Join(", ", have)}]. Resolve it by name (see TryOperator) " +
                                       "or the tool will not build for anyone on this version.");
                        missingMembers++;
                    }
                }
            }

            string verdict = missingTypes == 0 && missingMembers == 0
                ? $"[CckContract] OK: all {checkedMembers} member(s) across {Required.Length} type(s) present."
                : $"[CckContract] {missingTypes} type(s) and {missingMembers} member(s) MISSING.";
            Debug.Log(verdict);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(missingTypes + missingMembers == 0 ? 0 : 1);
            }
        }

        public static void RunBatch() => Run();
    }
}
#endif
