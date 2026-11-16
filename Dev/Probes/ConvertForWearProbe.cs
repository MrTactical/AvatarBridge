#if VRC_SDK_VRCSDK3 && CVR_CCK_EXISTS
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace AvatarBridge.Regression
{
    // Converts one avatar the way the window's Convert button does, with every option on, and
    // saves the scene so the result is there to upload. The previous conversion in the scene is
    // switched off and renamed, never deleted.
    // Batch: -executeMethod AvatarBridge.Regression.ConvertForWearProbe.Run -abScene <path> -abAvatar <root name>
    // -abSaved converts with the window's last-used settings instead, a reconvert by hand. -abNoSave
    // leaves the scene as it was on disk: the outputs are written as Convert writes them, and the
    // previous conversion stays in the scene untouched.
    public static class ConvertForWearProbe
    {
        public static void Run()
        {
            int code = 1;
            try
            {
                string scenePath = Arg("-abScene"), name = Arg("-abAvatar");
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var roots = scene.GetRootGameObjects();
                foreach (var old in roots.Where(r => r.name == name + " (ChilloutVR)"))
                {
                    old.name = name + " (ChilloutVR) previous";
                    old.SetActive(false);
                    Debug.Log($"[ConvertForWear] switched off the previous \"{name} (ChilloutVR)\"");
                }
                var source = roots.First(r => r.name == name);
                source.SetActive(true);
                var descriptor = source.GetComponentInChildren<VRCAvatarDescriptor>(true);
                var settings = Flag("-abSaved")
                    ? JsonUtility.FromJson<BridgeSettings>(EditorPrefs.GetString(CvrSetup.SettingsPrefsKey, "{}"))
                    : Everything();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var report = BridgeConverter.Convert(descriptor, settings);
                Debug.Log($"[ConvertForWear] converted in {clock.Elapsed.TotalMinutes:0.0} min: " +
                          string.Join(", ", Enum.GetValues(typeof(ReportStatus)).Cast<ReportStatus>()
                              .Select(s => $"{s} {report.CountOf(s)}")));
                foreach (var e in report.Entries.Where(e => e.Status == ReportStatus.Error || e.Status == ReportStatus.Warning))
                    Debug.Log($"[ConvertForWear]   {e.Status} [{e.Category}] {e.Subject}: {e.Detail}");
                foreach (var e in report.Entries.Where(e => e.Category == "YAPS" && e.Status == ReportStatus.Converted))
                    Debug.Log($"[ConvertForWear]   {e.Status} [{e.Category}] {e.Subject}: {e.Detail}");
                if (report.ConvertedRoot == null) throw new Exception("no converted root");
#if AVATARBRIDGE_YAPS
                foreach (var plug in report.ConvertedRoot.GetComponentsInChildren<Yaps.YapsPlug>(true))
                    foreach (var m in (plug.Target != null ? plug.Target.sharedMaterials : new Material[0]).Where(m => m != null && m.HasProperty("_YAPS_Bake")))
                        Debug.Log($"[ConvertForWear] plug \"{plug.name}\": \"{m.name}\" patched from \"{YapsShaderPatcher.SourceShaderOf(m)}\"" +
                                  (m.HasProperty("_PenetratorEnabled") ? $", DPS switch {m.GetFloat("_PenetratorEnabled")}" : ""));
                foreach (var socket in report.ConvertedRoot.GetComponentsInChildren<Yaps.YapsSocket>(true))
                    Debug.Log($"[ConvertForWear] socket \"{socket.name}\" ({socket.kind})");
#endif
                Debug.Log($"[ConvertForWear] output folder {System.IO.Path.GetDirectoryName(report.SavedReportPath)}");
                if (Flag("-abNoSave"))
                {
                    AssetDatabase.SaveAssets();
                    Debug.Log("[ConvertForWear] scene left unsaved");
                    code = report.CountOf(ReportStatus.Error) == 0 ? 0 : 2;
                    if (Application.isBatchMode) EditorApplication.Exit(code);
                    return;
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log($"[ConvertForWear] saved {scenePath} with \"{report.ConvertedRoot.name}\"");
                code = report.CountOf(ReportStatus.Error) == 0 ? 0 : 2;
            }
            catch (Exception e)
            {
                Debug.LogError("[ConvertForWear] threw: " + e);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        // Every bool on. Toggle style, physics target and face tracking stay on their defaults,
        // which are the full-featured choice of each.
        static BridgeSettings Everything()
        {
            var s = new BridgeSettings();
            foreach (var f in typeof(BridgeSettings).GetFields().Where(f => !f.IsLiteral && !f.IsStatic && f.FieldType == typeof(bool)))
                f.SetValue(s, true);
            foreach (var f in typeof(BridgeSettings).GetFields().Where(f => !f.IsLiteral && !f.IsStatic).OrderBy(f => f.Name))
                Debug.Log($"[ConvertForWear] setting {f.Name}={f.GetValue(s)}");
            return s;
        }

        static bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : throw new Exception("missing " + name);
        }
    }
}
#endif
