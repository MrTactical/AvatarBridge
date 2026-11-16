#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace AvatarBridge.Regression
{
    // A VRChat dropdown is a submenu of toggles that each set one int. Value 0 has no button (a toggle
    // switched off returns the int to 0), so it came out "Option 0", and the dropdown took its first
    // option's name: a "Tops" submenu became "Hoodie".
    public static class DropdownNamingTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Dropdowns are named after their submenu")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("DropdownNamingAvatar");
            var made = new List<Object>();
            try
            {
                VRCExpressionsMenu Menu(params VRCExpressionsMenu.Control[] controls)
                {
                    var m = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    m.controls = controls.ToList();
                    made.Add(m);
                    return m;
                }
                VRCExpressionsMenu.Control Toggle(string name, string parameter, float value) => new VRCExpressionsMenu.Control
                {
                    name = name, type = VRCExpressionsMenu.Control.ControlType.Toggle, value = value,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter },
                };
                VRCExpressionsMenu.Control Sub(string name, VRCExpressionsMenu menu) => new VRCExpressionsMenu.Control
                {
                    name = name, type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menu,
                };

                var tops = Menu(Toggle("Hoodie", "Top", 1), Toggle("Shirt", "Top", 2));
                var eyes = Menu(Toggle("Left Wink", "Eyes", 1), Toggle("Right Wink", "Eyes", 2));
                var hat = Menu(Toggle("Cap", "Cap", 1));
                var main = Menu(Sub("Tops", tops), Sub("Eye Expressions", eyes), Sub("Hats", hat));
                var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                made.Add(parameters);
                parameters.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter { name = "Top", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
                    new VRCExpressionParameters.Parameter { name = "Eyes", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
                    new VRCExpressionParameters.Parameter { name = "Cap", valueType = VRCExpressionParameters.ValueType.Int, networkSynced = true },
                };

                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.expressionsMenu = main;
                descriptor.expressionParameters = parameters;
                var avatar = root.AddComponent<CVRAvatar>();
                avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>() };
                var ctx = new BridgeContext { Settings = new BridgeSettings(), Report = new BridgeReport(), SourceDescriptor = descriptor, CvrAvatar = avatar };
                ParameterMenuConverter.Run(ctx);

                var entries = avatar.avatarSettings.settings;
                foreach (var e in entries) Debug.Log($"[DropdownNamingTest] entry \"{e.name}\" ({e.machineName}, {e.type})");
                var top = entries.FirstOrDefault(e => e.machineName == "Top");
                var eye = entries.FirstOrDefault(e => e.machineName == "Eyes");
                var cap = entries.FirstOrDefault(e => e.machineName == "Cap");
                Check(top != null && top.name == "Tops", $"the Top dropdown is titled \"Tops\" (got \"{top?.name}\")");
                Check(eye != null && eye.name == "Eye Expressions", $"the Eyes dropdown is titled \"Eye Expressions\" (got \"{eye?.name}\")");
                Check(cap != null && cap.name == "Cap", $"a lone toggle keeps its own name (got \"{cap?.name}\")");
                var options = (top?.setting as CVRAdvancesAvatarSettingGameObjectDropdown)?.options;
                string names = options == null ? "none" : string.Join(", ", options.Select(o => o.name));
                Check(options != null && options.Count == 3 && options[0].name == "None" && options[1].name == "Hoodie" && options[2].name == "Shirt",
                    $"options read None, Hoodie, Shirt (got {names})");
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (var o in made) Object.DestroyImmediate(o);
            }
            Debug.Log(failures == 0 ? "[DropdownNamingTest] PASS" : $"[DropdownNamingTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[DropdownNamingTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
