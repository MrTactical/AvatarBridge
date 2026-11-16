#if AVATARBRIDGE_YAPS
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AvatarBridge.Regression
{
    // YAPS Standard compiles every pass at 4.5 or above. Below it the squash cannot read its
    // skinned slots and falls back to the bone matrices, which never see the cloth swing.
    public static class StandardTargetTest
    {
        [MenuItem("Tools/Avatar Bridge/Dev/Tests/YAPS Standard reads the skinned slots")]
        public static void Run()
        {
            int fail = 0;
            void Check(bool ok, string what)
            {
                if (!ok) fail++;
                Debug.Log($"[StandardTargetTest] {(ok ? "ok   " : "WRONG")} {what}");
            }
            var shader = Shader.Find(YapsNativeBuilder.StandardName);
            string path = shader != null ? AssetDatabase.GetAssetPath(shader) : null;
            Check(!string.IsNullOrEmpty(path), "YAPS Standard is in the project");
            if (!string.IsNullOrEmpty(path))
            {
                var targets = Regex.Matches(File.ReadAllText(path), @"#pragma\s+target\s+([\d.]+)").Cast<Match>()
                    .Select(m => float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
                Check(targets.Count > 0 && targets.All(t => t >= 4.5f), $"every pass at 4.5 or above ({string.Join(", ", targets)})");
                Check(!ShaderUtil.ShaderHasError(shader), "and it compiles");
            }
            Debug.Log($"[StandardTargetTest] {(fail == 0 ? "PASS" : fail + " WRONG")}");
            if (Application.isBatchMode) EditorApplication.Exit(fail == 0 ? 0 : 1);
        }
    }
}
#endif
