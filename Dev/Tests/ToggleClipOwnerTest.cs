#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System.Collections.Generic;
using ABI.CCK.Components;
using ABI.CCK.Scripts;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // A new menu toggle never takes over a clip another toggle plays. The clip's file is named for
    // the avatar and the label, so a second avatar of the same name, or a new entry with an old
    // entry's label, rewrote the first one's clip and its toggle switched the wrong thing.
    public static class ToggleClipOwnerTest
    {
        static int failures;
        const string Name = "ToggleClipOwnerAvatar";

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/New toggle keeps off another's clip")]
        public static void Run()
        {
            failures = 0;
            GameObject first = null, second = null;
            string dir = YapsNativeBuilder.OutputRoot + "/" + Name;
            try
            {
                // Another avatar of the same name already plays a clip where the new toggle's would go.
                const string label = "Toggle Owner Plug plug";
                YapsNativeBuilder.EnsureFolderPublic(dir);
                var theirs = new AnimationClip();
                theirs.SetCurve("Elsewhere", typeof(MeshRenderer), "material._YAPS_Enabled", AnimationCurve.Constant(0, 1f / 60f, 1));
                AssetDatabase.CreateAsset(theirs, dir + "/" + label + " on.anim");
                first = new GameObject(Name);
                first.AddComponent<Animator>();
                var other = first.AddComponent<CVRAvatar>();
                other.avatarSettings = new CVRAdvancedAvatarSettings { settings = new List<CVRAdvancedSettingsEntry>(), initialized = true };
                other.avatarSettings.settings.Add(new CVRAdvancedSettingsEntry
                {
                    name = label, machineName = "Theirs", type = CVRAdvancedSettingsEntry.SettingsType.Toggle,
                    toggleSettings = new CVRAdvancesAvatarSettingGameObjectToggle { useAnimationClip = true, animationClip = theirs },
                });

                second = new GameObject(Name);
                second.AddComponent<Animator>();
                var avatar = second.AddComponent<CVRAvatar>();
                var plugObject = YapsNativeBuilder.BuildTestPlug(second.transform, select: false, name: "Toggle Owner Plug");
                var plug = plugObject.GetComponent<YapsPlug>();
                var material = plugObject.GetComponent<MeshRenderer>().sharedMaterial;
                Debug.Log("[ToggleClipOwnerTest] " + YapsToggles.EnsurePlugToggle(plug, avatar, material, YapsToggles.MenuLabelFor(plug)));

                var bindings = AnimationUtility.GetCurveBindings(theirs);
                Check(bindings.Length == 1 && bindings[0].path == "Elsewhere", $"the other avatar's clip is untouched ({(bindings.Length > 0 ? bindings[0].path : "none")})");
                var mine = avatar.avatarSettings?.settings?.Find(e => e.toggleSettings?.animationClip != null)?.toggleSettings.animationClip;
                Check(mine != null && mine != theirs, $"the new toggle has a clip of its own ({(mine != null ? AssetDatabase.GetAssetPath(mine) : "none")})");
            }
            finally
            {
                if (first != null) Object.DestroyImmediate(first);
                if (second != null) Object.DestroyImmediate(second);
                AssetDatabase.DeleteAsset(dir);
                AssetDatabase.DeleteAsset(YapsNativeBuilder.OutputRoot + "/Toggle Owner Plug");
                AssetDatabase.DeleteAsset(YapsNativeBuilder.OutputRoot + "/Test Plug/Toggle Owner Plug Mesh.asset");
            }
            Debug.Log(failures == 0 ? "[ToggleClipOwnerTest] PASS" : $"[ToggleClipOwnerTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ToggleClipOwnerTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
