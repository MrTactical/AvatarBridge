// Toggle style "CVR Native Targets" hands each plain object toggle to the
// CCK: its objects go into the menu entry and its layer is deleted. The
// dead-menu prune then found a parameter no layer read and deleted the
// entry too, so every toggle handed over left the menu. This converts one
// avatar that way and checks each one is still in the menu, still holds its
// objects, and is not reported as a control that does nothing.
//
// Run: -executeMethod AvatarBridge.Regression.NativeToggleProbe.Run -probeScene "Assets/.../Scene.unity"
#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Collections;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    public static class NativeToggleProbe
    {
        static int fail;

        public static void Run()
        {
            fail = 0;
            try
            {
                var args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-probeScene");
                EditorSceneManager.OpenScene(args[at + 1], OpenSceneMode.Single);
                var source = UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true).First();
                var report = BridgeConverter.Convert(source,
                    new BridgeSettings { toggleStyle = ToggleStyle.CvrNativeTargets, slimTexturesOnConvert = false });
                var avatar = report.ConvertedRoot.GetComponent<CVRAvatar>();

                var handed = report.Entries
                    .Where(e => e.Category == "Native toggles" && (e.Detail ?? "").Contains("toggled natively by CVR"))
                    .Select(e => e.Subject).Distinct().ToList();
                Check(handed.Count > 0, $"{handed.Count} toggle(s) handed to the CCK");
                var inert = AvatarSurvey.Build(avatar).Findings
                    .Where(f => f.Kind == "control that does nothing").Select(f => f.Subject).ToList();
                foreach (string name in handed)
                {
                    var entry = avatar.avatarSettings.settings.FirstOrDefault(s => s != null && s.name == name);
                    Check(entry != null && HasTargets(entry), $"\"{name}\" is in the menu with its objects");
                    Check(!report.Entries.Any(e => e.Subject == $"Menu entry \"{name}\" removed"),
                        $"\"{name}\" was not pruned as dead");
                    Check(!inert.Contains(name), $"\"{name}\" is not called a control that does nothing");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                fail++;
            }
            Log(fail == 0 ? "PASS" : $"FAIL: {fail} check(s)");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }

        // Read here rather than through the converter, so the probe also
        // runs against a build from before the fix.
        static bool HasTargets(CVRAdvancedSettingsEntry entry)
        {
            var field = entry.setting?.GetType().GetField("gameObjectTargets");
            return field?.GetValue(entry.setting) is IList list && list.Count > 0;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) fail++;
            Log((ok ? "ok   " : "FAIL ") + what);
        }

        static void Log(string s) => Debug.Log("[NativeToggleProbe] " + s);
    }
}
#endif
