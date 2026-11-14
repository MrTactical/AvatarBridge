#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace AvatarBridge.Regression
{
    // A parameter renamed to be CCK-safe ("Hide Arm Fluff" to HideArmFluff) kept the controller's 0
    // although the menu said 1: the defaults were looked up by the VRChat name after the rename, so
    // the avatar started on the wrong toggles until the game applied the saved menu.
    public static class RenamedDefaultTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Renamed parameters keep their menu default")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("RenamedDefaultAvatar");
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            try
            {
                parameters.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter { name = "Hide Arm Fluff", valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = 1, networkSynced = true },
                    new VRCExpressionParameters.Parameter { name = "Teeth Blendshapes", valueType = VRCExpressionParameters.ValueType.Int, defaultValue = 5, networkSynced = true },
                    new VRCExpressionParameters.Parameter { name = "Plain", valueType = VRCExpressionParameters.ValueType.Bool, defaultValue = 1, networkSynced = true },
                };
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.expressionParameters = parameters;
                var avatar = root.AddComponent<CVRAvatar>();
                avatar.avatarSettings = new CVRAdvancedAvatarSettings
                {
                    settings = new List<CVRAdvancedSettingsEntry>
                    {
                        new CVRAdvancedSettingsEntry { name = "Hide Arm Fluff", machineName = "HideArmFluff",
                            setting = new CVRAdvancesAvatarSettingGameObjectToggle { defaultValue = true } },
                        new CVRAdvancedSettingsEntry { name = "Teeth", machineName = "Teeth", type = CVRAdvancedSettingsEntry.SettingsType.Dropdown,
                            setting = new CVRAdvancesAvatarSettingGameObjectDropdown { defaultValue = 5 } },
                        new CVRAdvancedSettingsEntry { name = "Plain", machineName = "Plain",
                            setting = new CVRAdvancesAvatarSettingGameObjectToggle { defaultValue = true } },
                    },
                };
                var master = new AnimatorController();
                master.AddParameter("HideArmFluff", AnimatorControllerParameterType.Bool);
                master.AddParameter("Teeth", AnimatorControllerParameterType.Int);
                master.AddParameter("Plain", AnimatorControllerParameterType.Bool);
                var ctx = new BridgeContext { Settings = new BridgeSettings(), Report = new BridgeReport(), SourceDescriptor = descriptor, CvrAvatar = avatar };
                typeof(BridgeContext).Assembly.GetType("AvatarBridge.AnimatorMerger")
                    .GetMethod("ApplyParameterDefaults", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { master, ctx });

                var p = master.parameters.ToDictionary(x => x.name);
                Check(p["HideArmFluff"].defaultBool, $"HideArmFluff starts on, as its menu does (got {p["HideArmFluff"].defaultBool})");
                Check(p["Teeth"].defaultInt == 5, $"Teeth starts on option 5, as its menu does (got {p["Teeth"].defaultInt})");
                Check(p["Plain"].defaultBool, "a parameter never renamed still takes its default");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(parameters);
            }
            Debug.Log(failures == 0 ? "[RenamedDefaultTest] PASS" : $"[RenamedDefaultTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[RenamedDefaultTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
