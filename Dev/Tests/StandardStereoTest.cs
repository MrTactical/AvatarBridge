#if AVATARBRIDGE_YAPS
using System.IO;
using System.Linq;
using AvatarBridge.Yaps;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // YAPS Standard's passes return Unity's own Standard output structs, which carry the
    // stereo member and are filled by Unity's own vertex functions. The stereo check reads
    // only the project's files, never found those structs, and warned "may not render
    // correctly in VR" on every YAPS Standard copy.
    public static class StandardStereoTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/YAPS Standard reads as stereo")]
        public static void Run()
        {
            int fail = 0;
            void Check(string what, System.Collections.Generic.List<string> problems)
            {
                bool ok = problems.Count == 0;
                if (!ok) fail++;
                Debug.Log($"[StandardStereoTest] {(ok ? "ok   " : "WRONG")} {what} ({string.Join("; ", problems)})");
            }

            var shader = Shader.Find(YapsNativeBuilder.StandardName);
            string source = shader != null ? AssetDatabase.GetAssetPath(shader) : null;
            if (string.IsNullOrEmpty(source)) { fail++; Debug.Log("[StandardStereoTest] WRONG no YAPS Standard in the project"); }
            else Check("YAPS Standard itself", ShaderSpiPatcher.StereoProblems(source));

            // Patched copies the corpus left behind, when there are any.
            const string generated = "Assets/YAPS/Generated/Shaders";
            if (Directory.Exists(generated))
                foreach (var path in Directory.GetFiles(generated, "YapsStandard_YAPS_*.shader").Take(3))
                    Check(Path.GetFileName(path), ShaderSpiPatcher.StereoProblems(path.Replace('\\', '/')));

            Debug.Log($"[StandardStereoTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
