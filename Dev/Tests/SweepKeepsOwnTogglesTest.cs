#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Clean up leftovers takes a deleted socket's menu toggle and leaves the wearer's own. It took
    // every object toggle whose objects were all gone, YAPS-made or not.
    public static class SweepKeepsOwnTogglesTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Clean up leftovers keeps the wearer's own toggles")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("SweepOwnTogglesAvatar");
            try
            {
                root.AddComponent<Animator>();
                var avatar = root.AddComponent<CVRAvatar>();
                avatar.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
                avatar.avatarSettings.settings.Add(Toggle("Hat", "Hat"));
                avatar.avatarSettings.settings.Add(Toggle("Hips hole", "HipsHole"));

                var said = YapsRemover.Sweep(root.transform);
                var names = avatar.avatarSettings.settings.Select(e => e.name).ToList();
                Check(names.Contains("Hat"), "the wearer's own toggle stays: " + string.Join("; ", said));
                Check(!names.Contains("Hips hole"), "the deleted socket's toggle goes");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[SweepKeepsOwnTogglesTest] PASS" : $"[SweepKeepsOwnTogglesTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        // A toggle whose one object has been deleted.
        static CVRAdvancedSettingsEntry Toggle(string label, string machine)
        {
            var gone = new GameObject(label);
            var entry = new CVRAdvancedSettingsEntry
            {
                name = label,
                machineName = machine,
                type = CVRAdvancedSettingsEntry.SettingsType.Toggle,
                toggleSettings = new CVRAdvancesAvatarSettingGameObjectToggle
                {
                    usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool,
                    gameObjectTargets = new List<CVRAdvancedSettingsTargetEntryGameObject>
                    {
                        new CVRAdvancedSettingsTargetEntryGameObject { gameObject = gone, onState = true, treePath = label },
                    },
                },
            };
            Object.DestroyImmediate(gone);
            return entry;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[SweepKeepsOwnTogglesTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
