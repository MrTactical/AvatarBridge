#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    // The window hands the converter its own settings object, which it saves to EditorPrefs, per user
    // and not per project. Whatever the converter settles for one run must stay on its copy: written
    // back, a project without the add-on turned the penetration choice to Remove everywhere.
    public static class ConverterSettingsCopyTest
    {
        static int failures;

        [MenuItem("Tools/Avatar Bridge/Dev/Tests/Converter leaves the caller's settings alone")]
        public static void Run()
        {
            failures = 0;
            var root = new GameObject("SettingsCopyTestAvatar");
            try
            {
                root.AddComponent<Animator>();
                var descriptor = root.AddComponent<VRCAvatarDescriptor>();
                // The pair the other way round, which the converter settles for its run.
                var settings = new BridgeSettings { convertYapsSystems = true, stripSpsSystems = false, cloneAvatar = false };
                try { BridgeConverter.Convert(descriptor, settings); }
                catch (System.Exception e) { Debug.Log("[ConverterSettingsCopyTest] the convert threw, which is not what is tested: " + e.Message); }
                Check(settings.convertYapsSystems, "convert to YAPS still as the caller set it");
                Check(!settings.stripSpsSystems, "strip still as the caller set it");
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
            }
            Debug.Log(failures == 0 ? "[ConverterSettingsCopyTest] PASS" : $"[ConverterSettingsCopyTest] FAIL: {failures}");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void Check(bool ok, string what)
        {
            if (!ok) failures++;
            Debug.Log($"[ConverterSettingsCopyTest] {(ok ? "ok   " : "WRONG")} {what}");
        }
    }
}
#endif
