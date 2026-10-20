#if CVR_CCK_EXISTS && AVATARBRIDGE_YAPS
using System;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // Patches the named materials as squash and plugs do, and says what came back. For a corpus
    // avatar's shader moved into a probe project, where the corpus project itself cannot be opened.
    //
    //   -executeMethod AvatarBridge.Regression.PatchOneProbe.Run -patchMaterials "Assets/a.mat|Assets/b.mat"
    public static class PatchOneProbe
    {
        const string Tag = "[PatchOne] ";

        public static void Run()
        {
            int code = 0;
            try
            {
                string list = Environment.GetCommandLineArgs().SkipWhile(a => a != "-patchMaterials").Skip(1).FirstOrDefault();
                if (string.IsNullOrEmpty(list)) throw new ArgumentException("-patchMaterials is required");
                foreach (string path in list.Split('|'))
                {
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null) { Debug.Log(Tag + $"MISSING {path}"); code = 1; continue; }
                    var report = new BridgeReport();
                    var patched = YapsShaderPatcher.Patch(m, report, out string refusal, out int skipped);
                    Debug.Log(Tag + (patched != null
                        ? $"PATCHED {m.name} ({m.shader.name}) -> {patched.name}, {skipped} shadow passes left straight"
                        : $"REFUSED {m.name} ({m.shader.name}): {refusal}"));
                    foreach (var e in report.Entries) Debug.Log(Tag + $"  report: {e.Status} [{e.Category}] {e.Subject} | {e.Detail}");
                }
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
#endif
