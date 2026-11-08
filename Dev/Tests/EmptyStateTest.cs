#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using System.Linq;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A state with no motion and Write Defaults off puts the body in the
    // bicycle pose. A YAPS toggle wired into the shipped controller AND its
    // base came out like that in the shipped one: the second write replaced
    // the clips the first had just made. The survey has to name such states.
    public static class EmptyStateTest
    {
        const string Folder = "Assets/AvatarBridgeEmptyStateTest";
        const string AvatarName = "EmptyStateTestAvatar";
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Empty states are caught")]
        public static void Run()
        {
            failures = 0;
            string generated = YapsNativeBuilder.OutputRoot + "/" + AvatarName;
            var root = new GameObject(AvatarName);
            try
            {
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "AvatarBridgeEmptyStateTest");
                var shipped = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/Shipped.controller");
                var based = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/Base.controller");

                var ring = new GameObject("Ring");
                ring.transform.SetParent(root.transform, false);
                root.AddComponent<Animator>();
                var avatar = root.AddComponent<CVRAvatar>();
                avatar.overrides = new AnimatorOverrideController(shipped);
                var entry = new CVRAdvancedSettingsEntry
                {
                    name = "Ring",
                    machineName = "RingToggle",
                    setting = new CVRAdvancesAvatarSettingGameObjectToggle
                    {
                        usedType = CVRAdvancesAvatarSettingBase.ParameterType.Bool,
                        gameObjectTargets = new List<CVRAdvancedSettingsTargetEntryGameObject>
                        {
                            new CVRAdvancedSettingsTargetEntryGameObject { gameObject = ring, treePath = "Ring", onState = true },
                        },
                    },
                };
                avatar.avatarSettings = new CVRAdvancedAvatarSettings
                {
                    baseController = based,
                    settings = new List<CVRAdvancedSettingsEntry> { entry },
                };

                YapsAasAnimator.Wire(avatar, entry);
                foreach (var c in new[] { shipped, based })
                {
                    var layer = c.layers.FirstOrDefault(l => l.name == "RingToggle");
                    Check(layer != null, $"{c.name} got the toggle's layer");
                    if (layer == null) continue;
                    Check(layer.stateMachine.states.Length > 0 && layer.stateMachine.states.All(s => s.state.motion != null),
                        $"every state of {c.name}'s toggle plays a clip");
                }

                var hole = shipped.layers.First(l => l.name == "RingToggle").stateMachine.states[0].state;
                hole.motion = null;
                hole.writeDefaultValues = false;
                var kinds = AvatarSurvey.Build(avatar).Findings.Where(f => f.Subject.Contains(hole.name)).Select(f => f.Kind).ToList();
                Check(kinds.Contains("state that bicycles the body"), "an empty state with Write Defaults off is named as the bicycle pose");
                hole.writeDefaultValues = true;
                kinds = AvatarSurvey.Build(avatar).Findings.Where(f => f.Subject.Contains(hole.name)).Select(f => f.Kind).ToList();
                Check(kinds.Contains("state with no motion") && !kinds.Contains("state that bicycles the body"),
                    "with Write Defaults on it is named, but not as the bicycle pose");
            }
            finally
            {
                Object.DestroyImmediate(root);
                AssetDatabase.DeleteAsset(Folder);
                AssetDatabase.DeleteAsset(generated);
            }
            Debug.Log(failures == 0 ? "[EmptyStateTest] PASS" : $"[EmptyStateTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[EmptyStateTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
