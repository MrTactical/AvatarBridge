// A socket's menu toggle once defaulted to the socket's own state, on, and a rebuild kept an
// existing entry as it was, so every socket of an avatar set up then opened at once in game.
//
// Run: -executeMethod AvatarBridge.Regression.SocketToggleDefaultTest.Run
#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    public static class SocketToggleDefaultTest
    {
        [MenuItem("Tools/AvatarBridge Dev/Test: socket toggles default off")]
        public static void Run()
        {
            int fail = 0;
            var root = new GameObject("__SocketToggleDefaultTest");
            try
            {
                var avatar = root.AddComponent<CVRAvatar>();
                var socket = new GameObject("Chest ring");
                socket.transform.SetParent(root.transform, false);
                avatar.avatarUsesAdvancedSettings = true;
                avatar.avatarSettings = new CVRAdvancedAvatarSettings
                {
                    initialized = true,
                    settings = new List<CVRAdvancedSettingsEntry>
                    {
                        new CVRAdvancedSettingsEntry
                        {
                            name = "Chest ring", machineName = "ChestRing",
                            type = CVRAdvancedSettingsEntry.SettingsType.Toggle,
                            toggleSettings = new CVRAdvancesAvatarSettingGameObjectToggle
                            {
                                defaultValue = true,
                                usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool,
                                gameObjectTargets = new List<CVRAdvancedSettingsTargetEntryGameObject>
                                {
                                    new CVRAdvancedSettingsTargetEntryGameObject { gameObject = socket, onState = true, treePath = "Chest ring" },
                                },
                            },
                        },
                    },
                };
                string said = YapsToggles.EnsureObjectToggle(socket, avatar, "Chest ring");
                Debug.Log("[SocketToggleDefaultTest] rebuild: " + said);
                var entries = avatar.avatarSettings.settings;
                bool ok = entries.Count == 1 && !entries[0].toggleSettings.defaultValue;
                Debug.Log($"[SocketToggleDefaultTest] {(ok ? "ok  " : "WRONG")} an older socket toggle comes out off by default " +
                          $"({entries.Count} entries, default {entries[0].toggleSettings.defaultValue})");
                if (!ok) fail++;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(fail == 0 ? "[SocketToggleDefaultTest] PASS" : $"[SocketToggleDefaultTest] FAIL: {fail}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
