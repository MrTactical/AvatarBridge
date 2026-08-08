#if CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    /// <summary>
    /// Asserts that every ChilloutVR CCK enum member AvatarBridge names in source still exists in
    /// the installed CCK.
    ///
    /// A member written into the source binds at COMPILE time, so if the CCK renames one the whole
    /// tool stops building — not a degraded conversion, an uninstallable tool. That happened: 3.7.1
    /// would not compile for anyone whose CCK spells its comparison operators "MoreThen"/"LessThen"
    /// rather than "MoreThan"/"LessThan", and nothing here could have found it, because the compile
    /// check builds against whichever CCK is on this machine — by definition the one that agrees.
    ///
    /// This does not fix that. Nothing short of resolving every member by name would, and that
    /// costs type safety across the codebase for a risk that is only real where a name is genuinely
    /// ambiguous. What it does is make DRIFT VISIBLE HERE: run it after updating the CCK and before
    /// a release, and a rename is caught while it is still cheap, instead of by a user who cannot
    /// install.
    ///
    /// When one does move, resolve THAT member by name — see TryOperator in AnimatorMerger — rather
    /// than converting everything.
    ///
    /// The list is maintained by hand on purpose: it is the contract, and a generated one would
    /// drift silently with the source it was generated from.
    /// </summary>
    public static class CckContractProbe
    {
        static readonly (string Type, string[] Members)[] Required =
        {
            ("ABI.CCK.Components.AnimatorDriverTask+Operator", new[]
            {
                "Set", "Addition", "Subtraction", "Multiplication", "Division", "Power", "NotEqual",
                // "MoreThan" is deliberately absent: it is resolved by name because the CCK has
                // shipped it both ways. Listing it here would fail the check on half the versions
                // this tool is meant to support.
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

        [MenuItem("Tools/AvatarBridge Dev/Check the CCK still has what we name")]
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
                    Debug.LogError($"[CckContract] TYPE MISSING: {typeName} — the CCK no longer has it, " +
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
                        Debug.LogError($"[CckContract] MEMBER MISSING: {typeName}.{member} — this CCK has " +
                                       $"[{string.Join(", ", have)}]. Resolve it by name (see TryOperator) " +
                                       "or the tool will not build for anyone on this version.");
                        missingMembers++;
                    }
                }
            }

            string verdict = missingTypes == 0 && missingMembers == 0
                ? $"[CckContract] OK — all {checkedMembers} member(s) across {Required.Length} type(s) present."
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
